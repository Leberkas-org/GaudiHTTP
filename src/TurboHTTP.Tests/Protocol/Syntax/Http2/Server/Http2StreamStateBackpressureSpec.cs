using System.Buffers;
using TurboHTTP.Protocol.Multiplexed.Body;
using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server;

public sealed class Http2StreamStateBackpressureSpec
{
    private sealed class FakePausableEncoder : IPausableBodyEncoder
    {
        public int PauseCalls { get; private set; }
        public int ResumeCalls { get; private set; }
        public void Pause() => PauseCalls++;
        public void Resume() => ResumeCalls++;
        public void Start(Stream bodyStream, Action<object> onMessage) { }
        public void Dispose() { }
    }

    private static StreamBodyChunk<int> Chunk(int len)
    {
        var owner = MemoryPool<byte>.Shared.Rent(len);
        return new StreamBodyChunk<int>(1, owner, len);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.2")]
    public void Enqueue_should_pause_encoder_when_pending_reaches_max_buffer()
    {
        var enc = new FakePausableEncoder();
        var state = new StreamState();
        state.InitBodyEncoder(enc, maxOutboundBuffer: 100);

        state.EnqueueBodyChunk(Chunk(60));
        Assert.Equal(0, enc.PauseCalls);

        state.EnqueueBodyChunk(Chunk(60));

        Assert.Equal(1, enc.PauseCalls);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.2")]
    public void Dequeue_should_resume_encoder_when_drained_to_low_watermark()
    {
        var enc = new FakePausableEncoder();
        var state = new StreamState();
        state.InitBodyEncoder(enc, maxOutboundBuffer: 100);

        state.EnqueueBodyChunk(Chunk(60));
        state.EnqueueBodyChunk(Chunk(60));
        Assert.Equal(1, enc.PauseCalls);

        state.TryDequeueBodyChunk(out var c1);
        c1!.Owner.Dispose();
        Assert.Equal(0, enc.ResumeCalls);

        state.TryDequeueBodyChunk(out var c2);
        c2!.Owner.Dispose();
        Assert.Equal(1, enc.ResumeCalls);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.2")]
    public void Unlimited_buffer_should_never_pause()
    {
        var enc = new FakePausableEncoder();
        var state = new StreamState();
        state.InitBodyEncoder(enc, maxOutboundBuffer: 0);

        state.EnqueueBodyChunk(Chunk(1_000_000));

        Assert.Equal(0, enc.PauseCalls);
    }
}
