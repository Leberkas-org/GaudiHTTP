using System.Buffers;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server;

public sealed class Http2StreamStateBackpressureSpec
{
    private static StreamBodyChunk Chunk(int len)
    {
        var owner = MemoryPool<byte>.Shared.Rent(len);
        return new StreamBodyChunk(owner, len);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.2")]
    public void Enqueue_should_track_pending_bytes()
    {
        var state = new StreamState();
        state.MarkBodyDrainActive();

        state.EnqueueBodyChunk(Chunk(60));
        Assert.Equal(60, state.PendingOutboundBytes);
        Assert.True(state.HasPendingOutbound);

        state.EnqueueBodyChunk(Chunk(40));
        Assert.Equal(100, state.PendingOutboundBytes);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.2")]
    public void Dequeue_should_reduce_pending_bytes()
    {
        var state = new StreamState();
        state.MarkBodyDrainActive();

        state.EnqueueBodyChunk(Chunk(60));
        state.EnqueueBodyChunk(Chunk(40));

        state.TryDequeueBodyChunk(out var c1);
        c1!.Owner.Dispose();
        Assert.Equal(40, state.PendingOutboundBytes);

        state.TryDequeueBodyChunk(out var c2);
        c2!.Owner.Dispose();
        Assert.Equal(0, state.PendingOutboundBytes);
        Assert.False(state.HasPendingOutbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.2")]
    public void MarkBodyDrainComplete_should_signal_drain_finished()
    {
        var state = new StreamState();
        state.MarkBodyDrainActive();
        Assert.True(state.HasBodyDrain);
        Assert.False(state.IsBodyDrainComplete);

        state.MarkBodyDrainComplete();
        Assert.True(state.IsBodyDrainComplete);
    }
}
