using System.Buffers;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Tests.Protocol.Syntax;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Frames;

public sealed class Http3FrameDecoderMultiSegmentSpec
{
    [Fact(Timeout = 5000)]
    public void DecodeAll_should_parse_varint_header_split_across_segments()
    {
        var data = new byte[] { 0xDE, 0xAD };
        var frameBytes = EncodeH3Frame(FrameType.Data, data);
        var seq = SequenceHelper.CreateMultiSegment(frameBytes[..1], frameBytes[1..]);

        using var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(in seq, out var consumed);

        Assert.Single(frames);
        Assert.True(consumed.Equals(seq.End));
        var df = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(data, df.Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void DecodeAll_should_parse_payload_split_across_segments()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var frameBytes = EncodeH3Frame(FrameType.Data, data);
        var splitAt = frameBytes.Length - 3;
        var seq = SequenceHelper.CreateMultiSegment(frameBytes[..splitAt], frameBytes[splitAt..]);

        using var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(in seq, out var consumed);

        Assert.Single(frames);
        Assert.True(consumed.Equals(seq.End));
        var df = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(data, df.Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void DecodeAll_should_skip_unknown_frame_type_spanning_segment_boundary()
    {
        var unknownType = (byte)0x21;
        var payload = new byte[] { 0xAA, 0xBB, 0xCC };
        var unknownFrame = new byte[] { unknownType, (byte)payload.Length };
        var unknownFull = new byte[unknownFrame.Length + payload.Length];
        unknownFrame.CopyTo(unknownFull, 0);
        payload.CopyTo(unknownFull, unknownFrame.Length);

        var knownData = new byte[] { 0xFF };
        var knownFrame = EncodeH3Frame(FrameType.Data, knownData);

        var combined = new byte[unknownFull.Length + knownFrame.Length];
        unknownFull.CopyTo(combined, 0);
        knownFrame.CopyTo(combined, unknownFull.Length);

        var seq = SequenceHelper.CreateMultiSegment(
            combined[..3],
            combined[3..]);

        using var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(in seq, out var consumed);

        Assert.Single(frames);
        Assert.True(consumed.Equals(seq.End));
        var df = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(knownData, df.Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    public void DecodeAll_should_preserve_zero_copy_for_single_segment()
    {
        var data = new byte[] { 0xCA, 0xFE };
        var frameBytes = EncodeH3Frame(FrameType.Data, data);

        var seq = new ReadOnlySequence<byte>(frameBytes);
        using var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(in seq, out _);

        Assert.Single(frames);
        var df = Assert.IsType<DataFrame>(frames[0]);
        Assert.True(df.Data.Span.Overlaps(frameBytes.AsSpan()));
    }

    private static byte[] EncodeH3Frame(FrameType type, byte[] payload)
    {
        var typeLen = QuicVarInt.EncodedLength((long)type);
        var payloadLenLen = QuicVarInt.EncodedLength(payload.Length);
        var result = new byte[typeLen + payloadLenLen + payload.Length];
        var offset = QuicVarInt.Encode((long)type, result);
        offset += QuicVarInt.Encode(payload.Length, result.AsSpan(offset));
        payload.CopyTo(result, offset);
        return result;
    }
}
