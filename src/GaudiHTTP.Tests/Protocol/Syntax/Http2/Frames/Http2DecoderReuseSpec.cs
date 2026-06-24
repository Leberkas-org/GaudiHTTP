using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Frames;

/// <summary>
/// <see cref="FrameDecoder.Decode"/> returns the decoder's reused frame list (no per-call array
/// allocation). The client/server state machines consume it synchronously within the same actor
/// message; a caller that needs to hold a result across calls must snapshot it. These tests pin the
/// reuse behaviour and guard against leaking a prior call's frames on the early-return path.
/// </summary>
public sealed class Http2DecoderReuseSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Decode_should_decode_multiple_frames_in_order()
    {
        var bytes = Concat(
            new PingFrame(new byte[8], isAck: false).Serialize(),
            new WindowUpdateFrame(1, 65535).Serialize());

        var frames = new FrameDecoder().Decode(bytes);

        Assert.Equal(2, frames.Count);
        Assert.IsType<PingFrame>(frames[0]);
        var wu = Assert.IsType<WindowUpdateFrame>(frames[1]);
        Assert.Equal(65535, wu.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Decode_should_return_an_empty_list_for_an_incomplete_frame()
    {
        // Fewer than the 9-octet frame header: no complete frame is produced.
        var frames = new FrameDecoder().Decode(new byte[] { 0, 0, 5 });

        Assert.Empty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Decode_should_not_leak_frames_from_a_previous_call_when_nothing_new_decodes()
    {
        var decoder = new FrameDecoder();

        var first = decoder.Decode(new PingFrame(new byte[8], isAck: false).Serialize());
        Assert.Single(first);

        // An empty feed with no buffered remainder must not surface the previous call's frames.
        var second = decoder.Decode(Array.Empty<byte>());
        Assert.Empty(second);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Decode_should_reuse_the_same_list_instance_across_calls()
    {
        var decoder = new FrameDecoder();

        var first = decoder.Decode(new PingFrame(new byte[8], isAck: false).Serialize());
        var second = decoder.Decode(new PingFrame(new byte[8], isAck: true).Serialize());

        // No fresh collection is allocated per call — the reused list is returned directly.
        Assert.Same(first, second);
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }
}
