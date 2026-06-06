using System.Buffers;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Client;

public sealed class Http2ClientBodyBackpressureSpec
{
    private static StreamBodyChunk Chunk(int len)
    {
        var owner = MemoryPool<byte>.Shared.Rent(len);
        return new StreamBodyChunk(owner, len);
    }

    [Fact(Timeout = 5000)]
    public void StreamState_should_track_pending_outbound_bytes()
    {
        var state = new StreamState();

        Assert.False(state.HasPendingOutbound);
        Assert.Equal(0, state.PendingOutboundBytes);

        state.EnqueueBodyChunk(Chunk(48 * 1024));
        Assert.True(state.HasPendingOutbound);
        Assert.Equal(48 * 1024, state.PendingOutboundBytes);

        state.EnqueueBodyChunk(Chunk(32 * 1024));
        Assert.Equal(80 * 1024, state.PendingOutboundBytes);

        state.TryDequeueBodyChunk(out var c1);
        c1!.Owner.Dispose();
        Assert.Equal(32 * 1024, state.PendingOutboundBytes);

        state.TryDequeueBodyChunk(out var c2);
        c2!.Owner.Dispose();
        Assert.False(state.HasPendingOutbound);
        Assert.Equal(0, state.PendingOutboundBytes);
    }

    [Fact(Timeout = 5000)]
    public void StreamState_should_track_body_drain_lifecycle()
    {
        var state = new StreamState();

        Assert.False(state.HasBodyDrain);
        Assert.False(state.IsBodyDrainComplete);

        state.MarkBodyDrainActive();
        Assert.True(state.HasBodyDrain);
        Assert.False(state.IsBodyDrainComplete);

        state.MarkBodyDrainComplete();
        Assert.True(state.HasBodyDrain);
        Assert.True(state.IsBodyDrainComplete);
    }

    [Fact(Timeout = 5000)]
    public void StreamState_should_reset_body_drain_state()
    {
        var state = new StreamState();
        state.MarkBodyDrainActive();
        state.MarkBodyDrainComplete();

        state.Reset();

        Assert.False(state.HasBodyDrain);
        Assert.False(state.IsBodyDrainComplete);
    }

    [Fact(Timeout = 5000)]
    public void StreamState_should_prepend_body_chunk_before_existing_queue()
    {
        var state = new StreamState();
        var first = Chunk(100);
        var second = Chunk(200);
        var prepended = Chunk(50);

        state.EnqueueBodyChunk(first);
        state.EnqueueBodyChunk(second);
        state.PrependBodyChunk(prepended);

        state.TryDequeueBodyChunk(out var c1);
        Assert.Equal(50, c1!.Length);
        c1.Owner.Dispose();

        state.TryDequeueBodyChunk(out var c2);
        Assert.Equal(100, c2!.Length);
        c2.Owner.Dispose();

        state.TryDequeueBodyChunk(out var c3);
        Assert.Equal(200, c3!.Length);
        c3.Owner.Dispose();
    }
}
