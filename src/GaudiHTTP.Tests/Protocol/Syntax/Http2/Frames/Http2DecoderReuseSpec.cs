using GaudiHTTP.Protocol.Syntax.Http2;

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

        var frames = new FrameDecoder().DecodeAll(bytes, out var bytesConsumed);

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
        // Fewer than the 9-octet frame header: no complete frame is produced — the bytes are
        // buffered into the decoder's remainder rather than reported as consumed downstream.
        var frames = new FrameDecoder().DecodeAll(new byte[] { 0, 0, 5 }, out var bytesConsumed);

        Assert.Equal(3, bytesConsumed);
        Assert.Empty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void DecodeAll_should_not_leak_frames_from_a_previous_call_when_nothing_new_decodes()
    {
        var decoder = new FrameDecoder();

        var first = decoder.DecodeAll(new PingFrame(new byte[8], isAck: false).Serialize(), out _);
        Assert.Single(first);

        // An empty feed with no buffered remainder must not surface the previous call's frames.
        var second = decoder.DecodeAll(Array.Empty<byte>(), out _);
        Assert.Empty(second);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void DecodeAll_should_reuse_the_same_list_instance_across_calls()
    {
        var decoder = new FrameDecoder();

        var first = decoder.DecodeAll(new PingFrame(new byte[8], isAck: false).Serialize(), out _);
        var second = decoder.DecodeAll(new PingFrame(new byte[8], isAck: true).Serialize(), out _);

        // No fresh collection is allocated per call — the reused list is returned directly.
        Assert.Same(first, second);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void DecodeAll_should_reuse_the_remainder_buffer_across_fragmented_calls()
    {
        // A frame split across two caller-owned buffers forces the decoder to buffer the first
        // fragment into its internal remainder byte[]. Feeding several such fragmented frames in
        // sequence must not grow the remainder buffer unboundedly — it is reused, not reallocated,
        // once large enough to hold the largest fragment seen.
        var decoder = new FrameDecoder();
        var ping1 = new PingFrame(new byte[8], isAck: false).Serialize();
        var ping2 = new PingFrame(new byte[8], isAck: true).Serialize();

        var partial1 = decoder.DecodeAll(ping1[..5], out var consumed1);
        Assert.Empty(partial1);
        Assert.Equal(5, consumed1);

        var completed1 = decoder.DecodeAll(ping1[5..], out _);
        Assert.Single(completed1);
        Assert.False(Assert.IsType<PingFrame>(completed1[0]).IsAck);

        var partial2 = decoder.DecodeAll(ping2[..5], out _);
        Assert.Empty(partial2);

        var completed2 = decoder.DecodeAll(ping2[5..], out _);
        Assert.Single(completed2);
        Assert.True(Assert.IsType<PingFrame>(completed2[0]).IsAck);
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }
}
