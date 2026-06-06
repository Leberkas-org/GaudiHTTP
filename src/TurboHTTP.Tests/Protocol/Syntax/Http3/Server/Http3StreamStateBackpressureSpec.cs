using System.Buffers;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Syntax.Http3;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server;

public sealed class Http3StreamStateBackpressureSpec
{
    private static StreamBodyChunk Chunk(int len)
    {
        var owner = MemoryPool<byte>.Shared.Rent(len);
        return new StreamBodyChunk(owner, len);
    }

    [Fact(Timeout = 5000)]
    public void Enqueue_should_accumulate_pending_bytes()
    {
        var state = new StreamState();

        state.EnqueueBodyChunk(Chunk(60));
        Assert.Equal(60, state.PendingOutboundBytes);

        state.EnqueueBodyChunk(Chunk(40));
        Assert.Equal(100, state.PendingOutboundBytes);
    }

    [Fact(Timeout = 5000)]
    public void Dequeue_should_reduce_pending_bytes()
    {
        var state = new StreamState();

        state.EnqueueBodyChunk(Chunk(60));
        state.EnqueueBodyChunk(Chunk(40));

        state.TryDequeueBodyChunk(out var c1);
        c1!.Owner.Dispose();
        Assert.Equal(40, state.PendingOutboundBytes);

        state.TryDequeueBodyChunk(out var c2);
        c2!.Owner.Dispose();
        Assert.Equal(0, state.PendingOutboundBytes);
    }

    [Fact(Timeout = 5000)]
    public void Reset_should_clear_pending_bytes_and_drain_state()
    {
        var state = new StreamState();

        state.MarkBodyDrainActive();
        state.EnqueueBodyChunk(Chunk(120));

        state.Reset();

        Assert.Equal(0, state.PendingOutboundBytes);
        Assert.False(state.HasBodyDrain);
        Assert.False(state.IsBodyDrainComplete);
    }

    [Fact(Timeout = 5000)]
    public void MarkBodyDrainActive_should_set_drain_flags()
    {
        var state = new StreamState();

        state.MarkBodyDrainActive();

        Assert.True(state.HasBodyDrain);
        Assert.False(state.IsBodyDrainComplete);
    }

    [Fact(Timeout = 5000)]
    public void MarkBodyDrainComplete_should_set_complete_flag()
    {
        var state = new StreamState();

        state.MarkBodyDrainActive();
        state.MarkBodyDrainComplete();

        Assert.True(state.HasBodyDrain);
        Assert.True(state.IsBodyDrainComplete);
    }

    [Fact(Timeout = 5000)]
    public void InitBodyReader_should_set_HasBodyReader()
    {
        var state = new StreamState();
        var reader = new QueuedBodyReader(capacity: 4);
        reader.Reset();

        state.InitBodyReader(reader);

        Assert.True(state.HasBodyReader);
    }

    [Fact(Timeout = 5000)]
    public void DetachBodyReader_should_clear_HasBodyReader()
    {
        var state = new StreamState();
        var reader = new QueuedBodyReader(capacity: 4);
        reader.Reset();
        state.InitBodyReader(reader);

        state.DetachBodyReader();

        Assert.False(state.HasBodyReader);
    }

    [Fact(Timeout = 5000)]
    public void FeedBody_should_reject_when_exceeding_max_size()
    {
        var state = new StreamState();
        var reader = new QueuedBodyReader(capacity: 4);
        reader.Reset();
        state.InitBodyReader(reader, maxBodySize: 10);

        var data = new byte[11];
        Assert.Throws<HttpProtocolException>(() => state.FeedBody(data, endStream: false));
    }
}
