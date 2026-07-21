using System.Buffers;
using System.Buffers.Binary;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Tests.Protocol.Syntax;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Frames;

public sealed class Http2FrameDecoderMultiSegmentSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void DecodeAll_should_parse_header_split_across_two_segments()
    {
        var frame = new PingFrame(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, isAck: false).Serialize();
        var seq = SequenceHelper.CreateMultiSegment(frame[..5], frame[5..]);

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(in seq, out var consumed);

        Assert.Single(frames);
        Assert.True(consumed.Equals(seq.End));
        var ping = Assert.IsType<PingFrame>(frames[0]);
        Assert.False(ping.IsAck);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, ping.Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void DecodeAll_should_parse_payload_split_across_two_segments()
    {
        var frame = new WindowUpdateFrame(1, 65535).Serialize();
        var splitAt = 9 + 2;
        var seq = SequenceHelper.CreateMultiSegment(frame[..splitAt], frame[splitAt..]);

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(in seq, out var consumed);

        Assert.Single(frames);
        Assert.True(consumed.Equals(seq.End));
        var wu = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(1, wu.StreamId);
        Assert.Equal(65535, wu.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void DecodeAll_should_parse_multiple_frames_spanning_segment_boundaries()
    {
        var ping = new PingFrame(new byte[8], isAck: false).Serialize();
        var wu = new WindowUpdateFrame(0, 4096).Serialize();
        var combined = new byte[ping.Length + wu.Length];
        ping.CopyTo(combined, 0);
        wu.CopyTo(combined, ping.Length);

        var split1 = ping.Length - 3;
        var split2 = ping.Length + 5;
        var seq = SequenceHelper.CreateMultiSegment(
            combined[..split1],
            combined[split1..split2],
            combined[split2..]);

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(in seq, out var consumed);

        Assert.Equal(2, frames.Count);
        Assert.True(consumed.Equals(seq.End));
        Assert.IsType<PingFrame>(frames[0]);
        Assert.IsType<WindowUpdateFrame>(frames[1]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void DecodeAll_should_report_consumed_before_partial_frame_in_multi_segment()
    {
        var complete = new PingFrame(new byte[8], isAck: false).Serialize();
        var partial = new byte[] { 0, 0, 5, 0x01 };
        var combined = new byte[complete.Length + partial.Length];
        complete.CopyTo(combined, 0);
        partial.CopyTo(combined, complete.Length);

        var seq = SequenceHelper.CreateMultiSegment(
            combined[..10],
            combined[10..]);

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(in seq, out var consumed);

        Assert.Single(frames);
        Assert.IsType<PingFrame>(frames[0]);
        Assert.Equal(complete.Length, seq.Slice(0, consumed).Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void DecodeAll_should_preserve_zero_copy_for_single_segment()
    {
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var header = new byte[9];
        header[0] = 0;
        header[1] = 0;
        header[2] = (byte)data.Length;
        header[3] = (byte)FrameType.Data;
        header[4] = 0x01;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(5), 1);

        var frame = new byte[9 + data.Length];
        header.CopyTo(frame, 0);
        data.CopyTo(frame, 9);

        var seq = new ReadOnlySequence<byte>(frame);
        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(in seq, out _);

        Assert.Single(frames);
        var df = Assert.IsType<DataFrame>(frames[0]);
        Assert.True(df.Data.Span.Overlaps(frame.AsSpan()));
    }
}
