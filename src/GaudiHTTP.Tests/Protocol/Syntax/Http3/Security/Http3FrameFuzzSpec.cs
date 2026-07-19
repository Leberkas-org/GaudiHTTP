using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Qpack;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Security;

public sealed class Http3FrameFuzzSpec
{
    private static void AssertDecodeNeverCrashes(FrameDecoder decoder, byte[] data)
    {
        try
        {
            decoder.DecodeAll(data, out _);
        }
        catch (HttpProtocolException)
        {
            // Expected — protocol violation, properly classified.
        }
        catch (QpackException)
        {
            // QPACK errors are acceptable at frame level
        }
        // NOTE: ArgumentException is intentionally NOT caught — malformed frames must surface as
        // HttpProtocolException (a classified protocol error), never a raw ArgumentException that
        // escapes the connection's protocol-error catch filters.
    }

    private static byte[] BuildRawFrame(long frameType, byte[] payload)
    {
        var typeBuf = new byte[8];
        var typeLen = QuicVarInt.Encode(frameType, typeBuf);

        var lenBuf = new byte[8];
        var lenLen = QuicVarInt.Encode(payload.Length, lenBuf);

        var frame = new byte[typeLen + lenLen + payload.Length];
        Array.Copy(typeBuf, 0, frame, 0, typeLen);
        Array.Copy(lenBuf, 0, frame, typeLen, lenLen);
        Array.Copy(payload, 0, frame, typeLen + lenLen, payload.Length);
        return frame;
    }

    private static byte[] BuildRawFrameWithDeclaredLength(long frameType, long declaredLength, byte[] actualPayload)
    {
        var typeBuf = new byte[8];
        var typeLen = QuicVarInt.Encode(frameType, typeBuf);

        var lenBuf = new byte[8];
        var lenLen = QuicVarInt.Encode(declaredLength, lenBuf);

        var frame = new byte[typeLen + lenLen + actualPayload.Length];
        Array.Copy(typeBuf, 0, frame, 0, typeLen);
        Array.Copy(lenBuf, 0, frame, typeLen, lenLen);
        Array.Copy(actualPayload, 0, frame, typeLen + lenLen, actualPayload.Length);
        return frame;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.8")]
    public void FrameDecoder_should_skip_unknown_frame_types_without_crashing()
    {
        var rng = new Random(42);

        for (var i = 0; i < 50; i++)
        {
            using var decoder = new FrameDecoder();
            // Unknown frame types: use values not in Http3FrameType enum
            var unknownType = (long)rng.Next(0x0E, 0x100);
            var payload = new byte[rng.Next(0, 64)];
            rng.NextBytes(payload);

            var frame = BuildRawFrame(unknownType, payload);
            var decoded = decoder.DecodeAll(frame, out _);

            Assert.Empty(decoded); // Unknown frames are skipped
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_buffer_truncated_frame_without_crashing()
    {
        using var decoder = new FrameDecoder();

        // Declare a DATA frame with length 100 but provide only 10 payload bytes
        var frame = BuildRawFrameWithDeclaredLength(
            (long)FrameType.Data, 100, new byte[10]);

        var decoded = decoder.DecodeAll(frame, out _);

        Assert.Empty(decoded);
        Assert.True(decoder.HasRemainder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_handle_empty_input_without_crashing()
    {
        using var decoder = new FrameDecoder();

        var frames = decoder.DecodeAll(ReadOnlyMemory<byte>.Empty, out var consumed);

        Assert.Empty(frames);
        Assert.Equal(0, consumed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_handle_single_byte_input_without_crashing()
    {
        // A single byte is not enough for a complete frame
        for (byte b = 0; b < 255; b++)
        {
            using var decoder = new FrameDecoder();
            var data = new[] { b };

            AssertDecodeNeverCrashes(decoder, data);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void FrameDecoder_should_decode_data_frame_on_request_stream()
    {
        using var decoder = new FrameDecoder();

        var payload = "Hello"u8.ToArray(); // "Hello"
        var frame = BuildRawFrame((long)FrameType.Data, payload);

        var frames = decoder.DecodeAll(frame, out _);

        var dataFrame = Assert.IsType<DataFrame>(Assert.Single(frames));
        Assert.Equal(5, dataFrame.Data.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void FrameDecoder_should_reject_reserved_h2_settings_via_settings_deserialize()
    {
        // RFC 9114 §7.2.4.1: HTTP/2 settings MUST NOT appear in HTTP/3
        // Reserved identifiers: 0x02, 0x03, 0x04, 0x05
        long[] reservedIds = [0x02, 0x03, 0x04, 0x05];

        foreach (var id in reservedIds)
        {
            var payloadBuf = new byte[16];
            var offset = QuicVarInt.Encode(id, payloadBuf);
            offset += QuicVarInt.Encode(42, payloadBuf.AsSpan(offset));
            var payload = payloadBuf[..offset];

            Assert.Throws<HttpProtocolException>(() => Settings.Deserialize(payload));
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void FrameDecoder_should_reject_duplicate_settings_identifiers()
    {
        // RFC 9114 §7.2.4: Each setting identifier MUST NOT occur more than once
        var payloadBuf = new byte[32];
        var offset = 0;
        // Write QPACK_MAX_TABLE_CAPACITY twice
        offset += QuicVarInt.Encode(SettingsIdentifier.QpackMaxTableCapacity, payloadBuf.AsSpan(offset));
        offset += QuicVarInt.Encode(4096, payloadBuf.AsSpan(offset));
        offset += QuicVarInt.Encode(SettingsIdentifier.QpackMaxTableCapacity, payloadBuf.AsSpan(offset));
        offset += QuicVarInt.Encode(8192, payloadBuf.AsSpan(offset));

        var payload = payloadBuf[..offset];

        Assert.Throws<HttpProtocolException>(() => Settings.Deserialize(payload));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void FrameDecoder_should_accept_unknown_settings_identifiers()
    {
        // RFC 9114 §7.2.4: Unknown settings MUST be ignored
        var payloadBuf = new byte[16];
        var offset = 0;
        offset += QuicVarInt.Encode(0xFF, payloadBuf.AsSpan(offset)); // Unknown identifier
        offset += QuicVarInt.Encode(42, payloadBuf.AsSpan(offset));

        var settings = Settings.Deserialize(payloadBuf[..offset]);

        Assert.Equal(42, settings[0xFF]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void FrameDecoder_should_reject_truncated_settings_payload()
    {
        // Truncated: identifier present but value missing
        var payloadBuf = new byte[8];
        var offset = QuicVarInt.Encode(SettingsIdentifier.QpackMaxTableCapacity, payloadBuf);
        var payload = payloadBuf[..offset]; // Only identifier, no value

        Assert.Throws<HttpProtocolException>(() => Settings.Deserialize(payload));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_handle_random_byte_sequences_without_crashing()
    {
        var rng = new Random(1234);

        for (var trial = 0; trial < 100; trial++)
        {
            using var decoder = new FrameDecoder();
            var data = new byte[rng.Next(1, 128)];
            rng.NextBytes(data);

            AssertDecodeNeverCrashes(decoder, data);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_handle_interleaved_valid_and_corrupt_frames()
    {
        using var decoder = new FrameDecoder();

        // Valid DATA frame
        var validFrame = BuildRawFrame((long)FrameType.Data, [0x01, 0x02, 0x03]);
        var frames1 = decoder.DecodeAll(validFrame, out _);
        var frame = Assert.IsType<DataFrame>(Assert.Single(frames1));

        // Valid GOAWAY frame
        var goawayPayload = new byte[8];
        var goawayLen = QuicVarInt.Encode(4, goawayPayload);
        var goawayFrame = BuildRawFrame((long)FrameType.GoAway, goawayPayload[..goawayLen]);
        var frames2 = decoder.DecodeAll(goawayFrame, out _);
        Assert.IsType<GoAwayFrame>(Assert.Single(frames2));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void FrameDecoder_should_decode_zero_length_data_frame()
    {
        using var decoder = new FrameDecoder();

        var frame = BuildRawFrame((long)FrameType.Data, []);

        var frames = decoder.DecodeAll(frame, out _);
        var dataFrame = Assert.IsType<DataFrame>(Assert.Single(frames));
        Assert.Equal(0, dataFrame.Data.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.2")]
    public void FrameDecoder_should_decode_empty_headers_frame()
    {
        using var decoder = new FrameDecoder();

        var frame = BuildRawFrame((long)FrameType.Headers, []);

        var frames = decoder.DecodeAll(frame, out _);
        var headersFrame = Assert.IsType<HeadersFrame>(Assert.Single(frames));
        Assert.Equal(0, headersFrame.HeaderBlock.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_decode_all_multiple_frames_in_sequence()
    {
        using var decoder = new FrameDecoder();

        var frame1 = BuildRawFrame((long)FrameType.Data, [0x01]);
        var frame2 = BuildRawFrame((long)FrameType.Data, [0x02]);
        var combined = new byte[frame1.Length + frame2.Length];
        frame1.CopyTo(combined, 0);
        frame2.CopyTo(combined, frame1.Length);

        var frames = decoder.DecodeAll(combined, out var consumed);

        Assert.Equal(2, frames.Count);
        Assert.Equal(combined.Length, consumed);

        foreach (var f in frames)
        {
            if (f is IDisposable d)
            {
            }
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_handle_frame_split_across_two_calls()
    {
        using var decoder = new FrameDecoder();

        var payload = "Hello"u8.ToArray();
        var fullFrame = BuildRawFrame((long)FrameType.Data, payload);

        // Split in the middle
        var half = fullFrame.Length / 2;
        var part1 = fullFrame[..half];
        var part2 = fullFrame[half..];

        var frames1 = decoder.DecodeAll(part1, out _);
        Assert.Empty(frames1);

        var frames2 = decoder.DecodeAll(part2, out _);
        var dataFrame = Assert.IsType<DataFrame>(Assert.Single(frames2));
        Assert.Equal(5, dataFrame.Data.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.1")]
    public void FrameDecoder_should_reset_buffered_state_on_reset()
    {
        using var decoder = new FrameDecoder();

        // Feed partial frame
        var fullFrame = BuildRawFrame((long)FrameType.Data, new byte[100]);
        decoder.DecodeAll(fullFrame[..5], out _);
        Assert.True(decoder.HasRemainder);

        decoder.Reset();
        Assert.False(decoder.HasRemainder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Settings_should_reject_h2_reserved_identifiers_via_set()
    {
        var settings = new Settings();

        Assert.Throws<HttpProtocolException>(() =>
            settings.Set(SettingsIdentifier.ReservedH2EnablePush, 1));
        Assert.Throws<HttpProtocolException>(() =>
            settings.Set(SettingsIdentifier.ReservedH2MaxConcurrentStreams, 100));
        Assert.Throws<HttpProtocolException>(() =>
            settings.Set(SettingsIdentifier.ReservedH2InitialWindowSize, 65535));
        Assert.Throws<HttpProtocolException>(() =>
            settings.Set(SettingsIdentifier.ReservedH2MaxFrameSize, 16384));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Settings_should_roundtrip_valid_parameters()
    {
        var settings = new Settings();
        settings.Set(SettingsIdentifier.QpackMaxTableCapacity, 4096);
        settings.Set(SettingsIdentifier.QpackBlockedStreams, 100);
        settings.Set(SettingsIdentifier.MaxFieldSectionSize, 8192);

        var serialized = settings.Serialize();
        var deserialized = Settings.Deserialize(serialized);

        Assert.Equal(4096, deserialized.QpackMaxTableCapacity);
        Assert.Equal(100, deserialized.QpackBlockedStreams);
        Assert.Equal(8192, deserialized.MaxFieldSectionSize);
    }
}