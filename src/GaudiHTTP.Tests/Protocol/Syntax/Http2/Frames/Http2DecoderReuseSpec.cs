using System.Buffers;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Tests.Protocol.Syntax;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Frames;

/// <summary>
/// <see cref="FrameDecoder.DecodeAll"/> returns the decoder's reused frame list (no per-call array
/// allocation) and reuses its internal remainder buffer across calls when a partial frame must be
/// buffered. Callers pass a caller-owned <see cref="ReadOnlyMemory{T}"/> and retain buffer ownership;
/// the client/server state machines consume the returned list synchronously within the same actor
/// message, and a caller that needs to hold a result across calls must snapshot it. These tests pin
/// the reuse behaviour and guard against leaking a prior call's frames on the early-return path.
/// </summary>
public sealed class Http2DecoderReuseSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void DecodeAll_should_decode_multiple_frames_in_order()
    {
        var bytes = Concat(
            new PingFrame(new byte[8], isAck: false).Serialize(),
            new WindowUpdateFrame(1, 65535).Serialize());

        var input = new ReadOnlySequence<byte>(bytes);
        var frames = new FrameDecoder().DecodeAll(input, out var consumed);
        var bytesConsumed = input.Slice(0, consumed).Length;

        Assert.Equal(bytes.Length, bytesConsumed);
        Assert.Equal(2, frames.Count);
        Assert.IsType<PingFrame>(frames[0]);
        var wu = Assert.IsType<WindowUpdateFrame>(frames[1]);
        Assert.Equal(65535, wu.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void DecodeAll_should_return_an_empty_list_for_an_incomplete_frame()
    {
        var input = new ReadOnlySequence<byte>(new byte[] { 0, 0, 5 });
        var frames = new FrameDecoder().DecodeAll(input, out var consumed);

        Assert.True(consumed.Equals(input.Start));
        Assert.Empty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void DecodeAll_should_not_leak_frames_from_a_previous_call_when_nothing_new_decodes()
    {
        var decoder = new FrameDecoder();

        var first = decoder.DecodeAll(new ReadOnlySequence<byte>(new PingFrame(new byte[8], isAck: false).Serialize()), out _);
        Assert.Single(first);

        // An empty feed with no buffered remainder must not surface the previous call's frames.
        var second = decoder.DecodeAll(ReadOnlySequence<byte>.Empty, out _);
        Assert.Empty(second);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void DecodeAll_should_reuse_the_same_list_instance_across_calls()
    {
        var decoder = new FrameDecoder();

        var first = decoder.DecodeAll(new ReadOnlySequence<byte>(new PingFrame(new byte[8], isAck: false).Serialize()), out _);
        var second = decoder.DecodeAll(new ReadOnlySequence<byte>(new PingFrame(new byte[8], isAck: true).Serialize()), out _);

        // No fresh collection is allocated per call — the reused list is returned directly.
        Assert.Same(first, second);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void DecodeAll_should_decode_fragmented_frame_from_multi_segment_sequence()
    {
        var decoder = new FrameDecoder();
        var ping = new PingFrame(new byte[8], isAck: false).Serialize();

        var seq = SequenceHelper.CreateMultiSegment(ping[..5], ping[5..]);
        var frames = decoder.DecodeAll(in seq, out var consumed);

        Assert.Single(frames);
        Assert.IsType<PingFrame>(frames[0]);
        Assert.True(consumed.Equals(seq.End));
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }
}
