using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Streams.Stages.Encoding;

namespace TurboHttp.StreamTests.RFC9114;

/// <summary>
/// Tests the HTTP/3 frame encoder stage per RFC 9114 §7.
/// Verifies that DATA, HEADERS, SETTINGS, GOAWAY, and other frame types
/// are correctly serialised to the QUIC variable-length integer wire format.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http30EncoderStage"/>.
/// RFC 9114 §7.1: HTTP/3 frame format uses QUIC variable-length integer encoding
/// for both type and length fields (unlike HTTP/2's fixed 9-byte header).
/// </remarks>
public sealed class Http30EncoderStageTests : StreamTestBase
{
    private async Task<byte[]> EncodeAsync(Http3Frame frame)
    {
        var item = await Source.Single(frame)
            .Via(Flow.FromGraph(new Http30EncoderStage()))
            .RunWith(Sink.First<IOutputItem>(), Materializer);

        var dataItem = (DataItem)item;
        var bytes = dataItem.Memory.Memory.Span[..dataItem.Length].ToArray();
        dataItem.Memory.Dispose();
        return bytes;
    }

    private async Task<List<DataItem>> EncodeMultipleAsync(params Http3Frame[] frames)
    {
        var items = await Source.From(frames)
            .Via(Flow.FromGraph(new Http30EncoderStage()))
            .RunWith(Sink.Seq<IOutputItem>(), Materializer);

        return items.Cast<DataItem>().ToList();
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.1-30EN-001: DATA frame serialises type 0x00 + varint length + payload")]
    public async Task Should_EncodeDataFrame_WithCorrectTypeAndPayload()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var frame = new Http3DataFrame(payload);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x00, bytes[0]); // type = DATA (0x00), 1-byte varint
        Assert.Equal(payload.Length, bytes[1]); // length = 5, 1-byte varint
        Assert.Equal(payload, bytes[2..]); // payload follows
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.2-30EN-002: HEADERS frame serialises type 0x01 + varint length + header block")]
    public async Task Should_EncodeHeadersFrame_WithCorrectTypeAndHeaderBlock()
    {
        var headerBlock = new byte[] { 0x00, 0x00, 0x82, 0x84 };
        var frame = new Http3HeadersFrame(headerBlock);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x01, bytes[0]); // type = HEADERS (0x01)
        Assert.Equal(headerBlock.Length, bytes[1]); // length
        Assert.Equal(headerBlock, bytes[2..]);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.4-30EN-003: SETTINGS frame serialises type 0x04 + parameter pairs")]
    public async Task Should_EncodeSettingsFrame_WithParameterPairs()
    {
        var parameters = new List<(long, long)> { (0x06, 4096) };
        var frame = new Http3SettingsFrame(parameters);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x04, bytes[0]); // type = SETTINGS (0x04)
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.6-30EN-004: GOAWAY frame serialises type 0x06 + stream ID")]
    public async Task Should_EncodeGoAwayFrame_WithStreamId()
    {
        var frame = new Http3GoAwayFrame(4);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x06, bytes[0]); // type = GOAWAY (0x06)
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.7-30EN-005: MAX_PUSH_ID frame serialises type 0x0d + push ID")]
    public async Task Should_EncodeMaxPushIdFrame_WithPushId()
    {
        var frame = new Http3MaxPushIdFrame(10);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x0d, bytes[0]); // type = MAX_PUSH_ID (0x0d)
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.3-30EN-006: CANCEL_PUSH frame serialises type 0x03 + push ID")]
    public async Task Should_EncodeCancelPushFrame_WithPushId()
    {
        var frame = new Http3CancelPushFrame(7);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x03, bytes[0]); // type = CANCEL_PUSH (0x03)
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.2.5-30EN-007: PUSH_PROMISE frame serialises type 0x05 + push ID + header block")]
    public async Task Should_EncodePushPromiseFrame_WithPushIdAndHeaderBlock()
    {
        var headerBlock = new byte[] { 0x00, 0x00, 0x82 };
        var frame = new Http3PushPromiseFrame(1, headerBlock);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x05, bytes[0]); // type = PUSH_PROMISE (0x05)
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.1-30EN-008: Empty DATA frame serialises correctly")]
    public async Task Should_EncodeEmptyDataFrame_WithZeroLengthPayload()
    {
        var frame = new Http3DataFrame(ReadOnlyMemory<byte>.Empty);

        var bytes = await EncodeAsync(frame);

        Assert.Equal(frame.SerializedSize, bytes.Length);
        Assert.Equal(0x00, bytes[0]); // type = DATA
        Assert.Equal(0x00, bytes[1]); // length = 0
        Assert.Equal(2, bytes.Length); // just the 2-byte prefix
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.1-30EN-009: Multiple frames encode independently")]
    public async Task Should_EncodeMultipleFrames_Independently()
    {
        var headers = new Http3HeadersFrame(new byte[] { 0x82 });
        var data = new Http3DataFrame(new byte[] { 0x01, 0x02 });

        var items = await EncodeMultipleAsync(headers, data);

        Assert.Equal(2, items.Count);
        Assert.Equal(headers.SerializedSize, items[0].Length);
        Assert.Equal(data.SerializedSize, items[1].Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9114-7.1-30EN-010: Round-trip serialisation matches Http3Frame.Serialize()")]
    public async Task Should_MatchDirectSerialize_When_EncodedViaStage()
    {
        var frame = new Http3DataFrame(new byte[] { 0xAA, 0xBB, 0xCC });
        var expected = frame.Serialize();

        var bytes = await EncodeAsync(frame);

        Assert.Equal(expected, bytes);
    }
}
