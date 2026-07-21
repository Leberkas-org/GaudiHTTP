using System.Buffers;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Tests.Protocol.Syntax;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Frames;

public sealed class Http3FrameDecoderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_data_frame()
    {
        var original = new DataFrame(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE });
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var sequence = new ReadOnlySequence<byte>(wire);
        var frames = decoder.DecodeAll(sequence, out var consumed);

        Assert.Single(frames);
        var data = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(original.Data.ToArray(), data.Data.ToArray());
        Assert.Equal(wire.Length, sequence.GetOffset(consumed));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_headers_frame()
    {
        var headerBlock = new byte[] { 0x00, 0x00, 0x82, 0x87, 0x44, 0x88 };
        var original = new HeadersFrame(headerBlock);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(wire), out _);

        Assert.Single(frames);
        var headers = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(headerBlock, headers.HeaderBlock.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_cancel_push_frame()
    {
        var original = new CancelPushFrame(16383);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(wire), out _);

        Assert.Single(frames);
        var cp = Assert.IsType<CancelPushFrame>(frames[0]);
        Assert.Equal(16383, cp.PushId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_settings_frame()
    {
        var parameters = new List<(long, long)>
        {
            (0x06, 4096), // MAX_FIELD_SECTION_SIZE
            (0x01, 100), // QPACK_MAX_TABLE_CAPACITY
            (0x07, 50), // QPACK_BLOCKED_STREAMS
        };
        var original = new SettingsFrame(parameters);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(wire), out _);

        Assert.Single(frames);
        var settings = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Equal(3, settings.Parameters.Count);
        Assert.Equal((0x06L, 4096L), settings.Parameters[0]);
        Assert.Equal((0x01L, 100L), settings.Parameters[1]);
        Assert.Equal((0x07L, 50L), settings.Parameters[2]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_push_promise_frame()
    {
        var headerBlock = new byte[] { 0xAA, 0xBB, 0xCC };
        var original = new PushPromiseFrame(42, headerBlock);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(wire), out _);

        Assert.Single(frames);
        var pp = Assert.IsType<PushPromiseFrame>(frames[0]);
        Assert.Equal(42, pp.PushId);
        Assert.Equal(headerBlock, pp.HeaderBlock.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_goaway_frame()
    {
        var original = new GoAwayFrame(1_000_000);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(wire), out _);

        Assert.Single(frames);
        var goaway = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(1_000_000, goaway.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_max_push_id_frame()
    {
        var original = new MaxPushIdFrame(63);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(wire), out _);

        Assert.Single(frames);
        var mp = Assert.IsType<MaxPushIdFrame>(frames[0]);
        Assert.Equal(63, mp.PushId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_return_need_more_data_when_partial_type_varint()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(ReadOnlySequence<byte>.Empty, out _);

        Assert.Empty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_frame_from_multi_segment_sequence()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var original = new DataFrame(payload);
        var wire = original.Serialize();

        // Split at midpoint into a multi-segment ReadOnlySequence
        var mid = wire.Length / 2;
        var part1 = wire[..mid];
        var part2 = wire[mid..];

        var decoder = new FrameDecoder();
        var sequence = SequenceHelper.CreateMultiSegment(part1, part2);
        var frames = decoder.DecodeAll(sequence, out var consumed);

        Assert.Single(frames);
        var data = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(payload, data.Data.ToArray());
        Assert.Equal(sequence.Length, sequence.GetOffset(consumed));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_frame_from_per_byte_segments()
    {
        var original = new GoAwayFrame(256);
        var wire = original.Serialize();

        // Build a multi-segment sequence with one byte per segment
        var segments = new byte[wire.Length][];
        for (var i = 0; i < wire.Length; i++)
        {
            segments[i] = [wire[i]];
        }

        var decoder = new FrameDecoder();
        var sequence = SequenceHelper.CreateMultiSegment(segments);
        var frames = decoder.DecodeAll(sequence, out var consumed);

        Assert.Single(frames);
        var goaway = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(256, goaway.StreamId);
        Assert.Equal(sequence.Length, sequence.GetOffset(consumed));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_skip_unknown_frame_type()
    {
        // Encode an unknown frame type (0xFF) with a 3-byte payload
        var buf = new byte[16];
        var offset = 0;
        offset += QuicVarInt.Encode(0xFF, buf.AsSpan(offset)); // Unknown type
        offset += QuicVarInt.Encode(3, buf.AsSpan(offset)); // Length = 3
        buf[offset++] = 0xAA;
        buf[offset++] = 0xBB;
        buf[offset++] = 0xCC;

        var decoder = new FrameDecoder();
        var sequence = new ReadOnlySequence<byte>(buf.AsMemory(0, offset));
        var frames = decoder.DecodeAll(sequence, out var consumed);

        Assert.Empty(frames); // Unknown type → skipped, no frame emitted, but bytes consumed
        Assert.Equal(offset, sequence.GetOffset(consumed));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_all_multiple_frames()
    {
        var frames = new Http3Frame[]
        {
            new DataFrame(new byte[] { 0x01 }),
            new GoAwayFrame(0),
            new MaxPushIdFrame(63),
            new SettingsFrame(new List<(long, long)> { (0x06, 4096) }),
        };

        // Serialize all frames into a single buffer
        var totalSize = 0;
        foreach (var f in frames)
        {
            totalSize += f.SerializedSize;
        }

        var wire = new byte[totalSize];
        var offset = 0;
        foreach (var f in frames)
        {
            var span = wire.AsSpan(offset);
            offset += f.WriteTo(ref span);
        }

        var decoder = new FrameDecoder();
        var sequence = new ReadOnlySequence<byte>(wire);
        var decoded = decoder.DecodeAll(sequence, out var consumed);

        Assert.Equal(4, decoded.Count);
        Assert.Equal(totalSize, sequence.GetOffset(consumed));
        Assert.IsType<DataFrame>(decoded[0]);
        Assert.IsType<GoAwayFrame>(decoded[1]);
        Assert.IsType<MaxPushIdFrame>(decoded[2]);
        Assert.IsType<SettingsFrame>(decoded[3]);
    }
}
