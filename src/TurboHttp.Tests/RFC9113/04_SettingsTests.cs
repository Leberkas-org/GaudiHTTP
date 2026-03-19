using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// RFC 9113 §6.5 — SETTINGS Frames.
///
/// Tests verify that <see cref="Http2FrameDecoder"/> correctly decodes SETTINGS frames,
/// enforces wire-format constraints, and that RFC-required parameter validation rules
/// are correctly applied by conforming callers.
///
/// Covered:
///   §6.5  : ACK flag — SettingsFrame.IsAck
///   §6.5  : SETTINGS on non-zero stream → PROTOCOL_ERROR
///   §6.5  : SETTINGS ACK with payload → FRAME_SIZE_ERROR
///   §6.5  : Payload not multiple of 6 → FRAME_SIZE_ERROR
///   §6.5.2: MAX_FRAME_SIZE range [16384, 16777215]
///   §6.5.2: ENABLE_PUSH must be 0 or 1
///   §6.5.2: INITIAL_WINDOW_SIZE overflow (> 2^31−1) → FLOW_CONTROL_ERROR
///   §6.5  : Parameter round-trip (encode → decode → verify)
///   §6.5  : Unknown parameters silently decoded (§5.5)
/// </summary>
public sealed class Http2SettingsTests
{
    // =========================================================================
    // Helpers — RFC-mandated validators that the decoder delegates to the caller
    // =========================================================================

    /// <summary>
    /// RFC 9113 §6.5.2: SETTINGS_ENABLE_PUSH MUST be 0 or 1.
    /// Any other value is a connection error (PROTOCOL_ERROR).
    /// </summary>
    private static void EnforceEnablePush(IReadOnlyList<(SettingsParameter, uint)> parameters)
    {
        foreach (var (key, value) in parameters)
        {
            if (key == SettingsParameter.EnablePush && value > 1)
            {
                throw new Http2Exception(
                    $"RFC 9113 §6.5.2: SETTINGS_ENABLE_PUSH value {value} is invalid; must be 0 or 1.",
                    Http2ErrorCode.ProtocolError);
            }
        }
    }

    /// <summary>
    /// RFC 9113 §6.5.2: SETTINGS_INITIAL_WINDOW_SIZE MUST NOT exceed 2^31−1 (0x7FFFFFFF).
    /// Any larger value is a connection error (FLOW_CONTROL_ERROR).
    /// </summary>
    private static void EnforceInitialWindowSize(IReadOnlyList<(SettingsParameter, uint)> parameters)
    {
        foreach (var (key, value) in parameters)
        {
            if (key == SettingsParameter.InitialWindowSize && value > 0x7FFFFFFFu)
            {
                throw new Http2Exception(
                    $"RFC 9113 §6.5.2: SETTINGS_INITIAL_WINDOW_SIZE {value} exceeds the maximum 2^31−1.",
                    Http2ErrorCode.FlowControlError);
            }
        }
    }

    // =========================================================================
    // SS-001..003: ACK flag and stream-0 constraint (RFC 9113 §6.5)
    // =========================================================================

    /// RFC 9113 §6.5 — SETTINGS ACK flag decoded as IsAck=true
    [Fact(DisplayName = "RFC9113-6.5-SS-001: SETTINGS ACK flag decoded as IsAck=true")]
    public void SettingsAck_DecodedWithIsAckTrue()
    {
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(SettingsFrame.SettingsAck());

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.True(frame.IsAck);
        Assert.Empty(frame.Parameters);
        Assert.Equal(0, frame.StreamId);
    }

    /// RFC 9113 §6.5 — Non-ACK SETTINGS decoded with IsAck=false
    [Fact(DisplayName = "RFC9113-6.5-SS-002: Non-ACK SETTINGS decoded with IsAck=false")]
    public void Settings_NonAck_DecodedWithIsAckFalse()
    {
        var bytes = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16384u)]).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.False(frame.IsAck);
    }

    /// RFC 9113 §6.5 — SETTINGS on non-zero stream is connection PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.5-SS-003: SETTINGS on non-zero stream is connection PROTOCOL_ERROR")]
    public void Settings_OnNonZeroStream_IsProtocolError()
    {
        // Build a SETTINGS frame with stream ID = 1 (violates RFC 9113 §6.5).
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x00, // length = 0
            0x04,             // SETTINGS
            0x00,             // flags = 0
            0x00, 0x00, 0x00, 0x01, // stream = 1 — MUST be 0
        };
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(rawFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // =========================================================================
    // SS-004..005: Frame-size errors (RFC 9113 §6.5)
    // =========================================================================

    /// RFC 9113 §6.5 — SETTINGS ACK with non-empty payload is FRAME_SIZE_ERROR
    [Fact(DisplayName = "RFC9113-6.5-SS-004: SETTINGS ACK with non-empty payload is FRAME_SIZE_ERROR")]
    public void SettingsAck_WithPayload_IsFrameSizeError()
    {
        // SETTINGS ACK (flags=0x1) with 6-byte payload — violates §6.5.
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x06, // length = 6
            0x04,             // SETTINGS
            0x01,             // ACK flag
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x00, 0x05, 0x00, 0x00, 0x40, 0x00, // MaxFrameSize=16384
        };
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(rawFrame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.5 — SETTINGS payload not a multiple of 6 is FRAME_SIZE_ERROR
    [Fact(DisplayName = "RFC9113-6.5-SS-005: SETTINGS payload not a multiple of 6 is FRAME_SIZE_ERROR")]
    public void Settings_PayloadNotMultipleOf6_IsFrameSizeError()
    {
        // Payload length = 7 (not a multiple of 6) — violates §6.5.
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x07, // length = 7
            0x04,             // SETTINGS
            0x00,             // flags = 0
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x00, 0x01, 0x00, 0x00, 0x10, 0x00, 0x00, // 7 bytes
        };
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(rawFrame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // =========================================================================
    // SS-006..009: MAX_FRAME_SIZE range [16384, 16777215] (RFC 9113 §6.5.2)
    // =========================================================================

    /// RFC 9113 §6.5.2 — MAX_FRAME_SIZE below 16384 is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.5-SS-006: MAX_FRAME_SIZE below 16384 is PROTOCOL_ERROR")]
    public void Settings_MaxFrameSize_BelowMin_IsProtocolError()
    {
        var bytes = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16383u)]).Serialize();
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(bytes));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.5.2 — MAX_FRAME_SIZE above 16777215 is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.5-SS-007: MAX_FRAME_SIZE above 16777215 is PROTOCOL_ERROR")]
    public void Settings_MaxFrameSize_AboveMax_IsProtocolError()
    {
        var bytes = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16777216u)]).Serialize();
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(bytes));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.5.2 — MAX_FRAME_SIZE at minimum (16384) is accepted
    [Fact(DisplayName = "RFC9113-6.5-SS-008: MAX_FRAME_SIZE at minimum (16384) is accepted")]
    public void Settings_MaxFrameSize_AtMin_Accepted()
    {
        var bytes = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16384u)]).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Contains(frame.Parameters, p => p.Item1 == SettingsParameter.MaxFrameSize && p.Item2 == 16384u);
    }

    /// RFC 9113 §6.5.2 — MAX_FRAME_SIZE at maximum (16777215) is accepted
    [Fact(DisplayName = "RFC9113-6.5-SS-009: MAX_FRAME_SIZE at maximum (16777215) is accepted")]
    public void Settings_MaxFrameSize_AtMax_Accepted()
    {
        var bytes = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16777215u)]).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Contains(frame.Parameters, p => p.Item1 == SettingsParameter.MaxFrameSize && p.Item2 == 16777215u);
    }

    // =========================================================================
    // SS-010..013: ENABLE_PUSH validation (RFC 9113 §6.5.2)
    // =========================================================================

    /// RFC 9113 §6.5.2 — ENABLE_PUSH=0 is accepted
    [Fact(DisplayName = "RFC9113-6.5-SS-010: ENABLE_PUSH=0 is accepted")]
    public void Settings_EnablePush_Zero_Accepted()
    {
        var bytes = new SettingsFrame([(SettingsParameter.EnablePush, 0u)]).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        // RFC 9113 §6.5.2: ENABLE_PUSH=0 is valid — must not throw.
        EnforceEnablePush(frame.Parameters);
        Assert.Contains(frame.Parameters, p => p.Item1 == SettingsParameter.EnablePush && p.Item2 == 0u);
    }

    /// RFC 9113 §6.5.2 — ENABLE_PUSH=1 is accepted
    [Fact(DisplayName = "RFC9113-6.5-SS-011: ENABLE_PUSH=1 is accepted")]
    public void Settings_EnablePush_One_Accepted()
    {
        var bytes = new SettingsFrame([(SettingsParameter.EnablePush, 1u)]).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        // RFC 9113 §6.5.2: ENABLE_PUSH=1 is valid — must not throw.
        EnforceEnablePush(frame.Parameters);
    }

    /// RFC 9113 §6.5.2 — ENABLE_PUSH=2 is connection PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.5-SS-012: ENABLE_PUSH=2 is connection PROTOCOL_ERROR")]
    public void Settings_EnablePush_Two_IsProtocolError()
    {
        var bytes = new SettingsFrame([(SettingsParameter.EnablePush, 2u)]).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        // RFC 9113 §6.5.2: ENABLE_PUSH > 1 MUST trigger a connection PROTOCOL_ERROR.
        var ex = Assert.Throws<Http2Exception>(() => EnforceEnablePush(frame.Parameters));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.5.2 — ENABLE_PUSH=0xFFFFFFFF is connection PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.5-SS-013: ENABLE_PUSH=0xFFFFFFFF is connection PROTOCOL_ERROR")]
    public void Settings_EnablePush_MaxValue_IsProtocolError()
    {
        var bytes = new SettingsFrame([(SettingsParameter.EnablePush, 0xFFFFFFFFu)]).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        var ex = Assert.Throws<Http2Exception>(() => EnforceEnablePush(frame.Parameters));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // =========================================================================
    // SS-014..016: INITIAL_WINDOW_SIZE overflow (RFC 9113 §6.5.2)
    // =========================================================================

    /// RFC 9113 §6.5.2 — INITIAL_WINDOW_SIZE above 2^31−1 is FLOW_CONTROL_ERROR
    [Fact(DisplayName = "RFC9113-6.5-SS-014: INITIAL_WINDOW_SIZE above 2^31-1 is FLOW_CONTROL_ERROR")]
    public void Settings_InitialWindowSize_Overflow_IsFlowControlError()
    {
        var bytes = new SettingsFrame([(SettingsParameter.InitialWindowSize, 0x80000000u)]).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        // RFC 9113 §6.5.2: INITIAL_WINDOW_SIZE > 2^31−1 MUST trigger a connection FLOW_CONTROL_ERROR.
        var ex = Assert.Throws<Http2Exception>(() => EnforceInitialWindowSize(frame.Parameters));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.5.2 — INITIAL_WINDOW_SIZE at exactly 2^31−1 is accepted
    [Fact(DisplayName = "RFC9113-6.5-SS-015: INITIAL_WINDOW_SIZE at exactly 2^31-1 is accepted")]
    public void Settings_InitialWindowSize_AtMax_Accepted()
    {
        var bytes = new SettingsFrame([(SettingsParameter.InitialWindowSize, 0x7FFFFFFFu)]).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        // RFC 9113 §6.5.2: 2^31−1 is the maximum valid value — must not throw.
        EnforceInitialWindowSize(frame.Parameters);
        Assert.Contains(frame.Parameters, p => p.Item1 == SettingsParameter.InitialWindowSize && p.Item2 == 0x7FFFFFFFu);
    }

    /// RFC 9113 §6.5.2 — INITIAL_WINDOW_SIZE at max uint is FLOW_CONTROL_ERROR
    [Fact(DisplayName = "RFC9113-6.5-SS-016: INITIAL_WINDOW_SIZE at max uint is FLOW_CONTROL_ERROR")]
    public void Settings_InitialWindowSize_MaxUint_IsFlowControlError()
    {
        var bytes = new SettingsFrame([(SettingsParameter.InitialWindowSize, 0xFFFFFFFFu)]).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        var ex = Assert.Throws<Http2Exception>(() => EnforceInitialWindowSize(frame.Parameters));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // =========================================================================
    // SS-017..020: Parameter parsing and round-trip (RFC 9113 §6.5)
    // =========================================================================

    /// RFC 9113 §6.5 — Empty SETTINGS frame decoded with no parameters
    [Fact(DisplayName = "RFC9113-6.5-SS-017: Empty SETTINGS frame decoded with no parameters")]
    public void Settings_EmptyPayload_DecodedWithNoParameters()
    {
        var emptySettings = new byte[]
        {
            0x00, 0x00, 0x00, // length = 0
            0x04,             // SETTINGS
            0x00,             // flags = 0 (no ACK)
            0x00, 0x00, 0x00, 0x00, // stream = 0
        };
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(emptySettings);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Empty(frame.Parameters);
        Assert.False(frame.IsAck);
    }

    /// RFC 9113 §6.5 — SETTINGS with multiple parameters all decoded correctly
    [Fact(DisplayName = "RFC9113-6.5-SS-018: SETTINGS with multiple parameters decoded correctly")]
    public void Settings_MultipleParameters_AllDecoded()
    {
        var bytes = new SettingsFrame([
            (SettingsParameter.HeaderTableSize, 2048u),
            (SettingsParameter.EnablePush, 0u),
            (SettingsParameter.MaxFrameSize, 32768u),
        ]).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Equal(3, frame.Parameters.Count);
        Assert.Contains(frame.Parameters, p => p.Item1 == SettingsParameter.HeaderTableSize && p.Item2 == 2048u);
        Assert.Contains(frame.Parameters, p => p.Item1 == SettingsParameter.EnablePush && p.Item2 == 0u);
        Assert.Contains(frame.Parameters, p => p.Item1 == SettingsParameter.MaxFrameSize && p.Item2 == 32768u);
    }

    /// RFC 9113 §6.5 / §5.5 — Unknown SETTINGS parameter is silently decoded
    [Fact(DisplayName = "RFC9113-6.5-SS-019: Unknown SETTINGS parameter is silently decoded (§5.5)")]
    public void Settings_UnknownParameter_SilentlyDecoded()
    {
        // Unknown parameter ID 0xFFFF, value = 42 — must not throw.
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x06, // length = 6
            0x04,             // SETTINGS
            0x00,             // flags = 0
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0xFF, 0xFF,       // unknown parameter = 0xFFFF
            0x00, 0x00, 0x00, 0x2A, // value = 42
        };
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(rawFrame);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        // Unknown parameter is decoded without error per RFC 9113 §5.5.
        Assert.Single(frame.Parameters);
    }

    /// RFC 9113 §6.5 — SETTINGS round-trip: encode then decode preserves all parameters
    [Fact(DisplayName = "RFC9113-6.5-SS-020: SETTINGS round-trip preserves all parameters")]
    public void Settings_RoundTrip_PreservesAllParameters()
    {
        var original = new SettingsFrame([
            (SettingsParameter.HeaderTableSize, 4096u),
            (SettingsParameter.MaxConcurrentStreams, 100u),
            (SettingsParameter.InitialWindowSize, 65535u),
            (SettingsParameter.MaxFrameSize, 16384u),
        ]);
        var bytes = original.Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var decoded = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Equal(4, decoded.Parameters.Count);
        Assert.Contains(decoded.Parameters, p => p.Item1 == SettingsParameter.HeaderTableSize && p.Item2 == 4096u);
        Assert.Contains(decoded.Parameters, p => p.Item1 == SettingsParameter.MaxConcurrentStreams && p.Item2 == 100u);
        Assert.Contains(decoded.Parameters, p => p.Item1 == SettingsParameter.InitialWindowSize && p.Item2 == 65535u);
        Assert.Contains(decoded.Parameters, p => p.Item1 == SettingsParameter.MaxFrameSize && p.Item2 == 16384u);
    }
}
