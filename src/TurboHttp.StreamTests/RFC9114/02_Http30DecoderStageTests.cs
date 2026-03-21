using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Streams.Stages.Decoding;

namespace TurboHttp.StreamTests.RFC9114;

/// <summary>
/// Tests the HTTP/3 frame decoder stage per RFC 9114 §7.
/// Verifies that binary-encoded HTTP/3 frames are correctly parsed from byte streams
/// including partial frames and unknown frame type handling.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30DecoderStage"/>.
/// RFC 9114 §7.1: HTTP/3 frame format uses QUIC variable-length integer encoding
/// for both type and length fields.
/// </remarks>
public sealed class Http30DecoderStageTests : StreamTestBase
{
    private static IInputItem Chunk(byte[] data)
        => new DataItem(new SimpleMemoryOwner(data), data.Length) { Key = RequestEndpoint.Default };

    private async Task<IReadOnlyList<Http3Frame>> DecodeAsync(params byte[][] chunks)
    {
        var source = Source.From(chunks.Select(Chunk));
        return await source
            .Via(Flow.FromGraph(new Http30DecoderStage()))
            .RunWith(Sink.Seq<Http3Frame>(), Materializer);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.1-30DE-001: DATA frame decoded with correct payload")]
    public async Task Should_DecodeDataFrame_When_CompleteFrameArrives()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var frame = new Http3DataFrame(payload);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var dataFrame = Assert.IsType<Http3DataFrame>(frames[0]);
        Assert.Equal(payload, dataFrame.Data.ToArray());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.2-30DE-002: HEADERS frame decoded with correct header block")]
    public async Task Should_DecodeHeadersFrame_When_CompleteFrameArrives()
    {
        var headerBlock = new byte[] { 0x00, 0x00, 0x82, 0x84 };
        var frame = new Http3HeadersFrame(headerBlock);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var headersFrame = Assert.IsType<Http3HeadersFrame>(frames[0]);
        Assert.Equal(headerBlock, headersFrame.HeaderBlock.ToArray());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.4-30DE-003: SETTINGS frame decoded with parameter pairs")]
    public async Task Should_DecodeSettingsFrame_WithParameterPairs()
    {
        var parameters = new List<(long, long)> { (0x06, 4096) };
        var frame = new Http3SettingsFrame(parameters);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var settingsFrame = Assert.IsType<Http3SettingsFrame>(frames[0]);
        Assert.Single(settingsFrame.Parameters);
        Assert.Equal(0x06, settingsFrame.Parameters[0].Identifier);
        Assert.Equal(4096, settingsFrame.Parameters[0].Value);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.6-30DE-004: GOAWAY frame decoded with stream ID")]
    public async Task Should_DecodeGoAwayFrame_WithStreamId()
    {
        var frame = new Http3GoAwayFrame(4);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var goAwayFrame = Assert.IsType<Http3GoAwayFrame>(frames[0]);
        Assert.Equal(4, goAwayFrame.StreamId);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.7-30DE-005: MAX_PUSH_ID frame decoded with push ID")]
    public async Task Should_DecodeMaxPushIdFrame_WithPushId()
    {
        var frame = new Http3MaxPushIdFrame(10);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var maxPushIdFrame = Assert.IsType<Http3MaxPushIdFrame>(frames[0]);
        Assert.Equal(10, maxPushIdFrame.PushId);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.3-30DE-006: CANCEL_PUSH frame decoded with push ID")]
    public async Task Should_DecodeCancelPushFrame_WithPushId()
    {
        var frame = new Http3CancelPushFrame(7);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var cancelPushFrame = Assert.IsType<Http3CancelPushFrame>(frames[0]);
        Assert.Equal(7, cancelPushFrame.PushId);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.5-30DE-007: PUSH_PROMISE frame decoded with push ID and header block")]
    public async Task Should_DecodePushPromiseFrame_WithPushIdAndHeaderBlock()
    {
        var headerBlock = new byte[] { 0x00, 0x00, 0x82 };
        var frame = new Http3PushPromiseFrame(1, headerBlock);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var pushPromiseFrame = Assert.IsType<Http3PushPromiseFrame>(frames[0]);
        Assert.Equal(1, pushPromiseFrame.PushId);
        Assert.Equal(headerBlock, pushPromiseFrame.HeaderBlock.ToArray());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.1-30DE-008: Empty DATA frame decoded correctly")]
    public async Task Should_DecodeEmptyDataFrame_WithZeroLengthPayload()
    {
        var frame = new Http3DataFrame(ReadOnlyMemory<byte>.Empty);
        var rawBytes = frame.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var dataFrame = Assert.IsType<Http3DataFrame>(frames[0]);
        Assert.Empty(dataFrame.Data.ToArray());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.1-30DE-009: Two frames in one chunk each decoded")]
    public async Task Should_DecodeBothFrames_When_TwoFramesArriveInOneChunk()
    {
        var headersBytes = new Http3HeadersFrame(new byte[] { 0x82 }).Serialize();
        var dataBytes = new Http3DataFrame(new byte[] { 0x01, 0x02 }).Serialize();

        var combined = new byte[headersBytes.Length + dataBytes.Length];
        headersBytes.CopyTo(combined, 0);
        dataBytes.CopyTo(combined, headersBytes.Length);

        var frames = await DecodeAsync(combined);

        Assert.Equal(2, frames.Count);
        Assert.IsType<Http3HeadersFrame>(frames[0]);
        Assert.IsType<Http3DataFrame>(frames[1]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.1-30DE-010: Frame split across two chunks reassembled")]
    public async Task Should_ReassembleFrame_When_SplitAcrossTwoChunks()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var rawBytes = new Http3DataFrame(payload).Serialize();

        var splitAt = rawBytes.Length / 2;
        var chunk1 = rawBytes[..splitAt];
        var chunk2 = rawBytes[splitAt..];

        var frames = await DecodeAsync(chunk1, chunk2);

        Assert.Single(frames);
        var dataFrame = Assert.IsType<Http3DataFrame>(frames[0]);
        Assert.Equal(payload, dataFrame.Data.ToArray());
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.1-30DE-011: Round-trip encode→decode preserves frame content")]
    public async Task Should_PreserveContent_When_RoundTripping()
    {
        var original = new Http3DataFrame(new byte[] { 0xAA, 0xBB, 0xCC });
        var rawBytes = original.Serialize();

        var frames = await DecodeAsync(rawBytes);

        Assert.Single(frames);
        var decoded = Assert.IsType<Http3DataFrame>(frames[0]);
        Assert.Equal(original.Data.ToArray(), decoded.Data.ToArray());
    }
}
