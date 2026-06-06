using System.Buffers;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Syntax.Http3;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client;

public sealed class Http3ClientBodyBackpressureSpec
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

        state.EnqueueBodyChunk(Chunk(48 * 1024));
        Assert.Equal(48 * 1024, state.PendingOutboundBytes);

        state.EnqueueBodyChunk(Chunk(48 * 1024));
        Assert.Equal(96 * 1024, state.PendingOutboundBytes);
    }

    [Fact(Timeout = 5000)]
    public void Dequeue_should_reduce_pending_bytes()
    {
        var state = new StreamState();

        state.EnqueueBodyChunk(Chunk(48 * 1024));
        state.EnqueueBodyChunk(Chunk(48 * 1024));

        state.TryDequeueBodyChunk(out var c1);
        c1!.Owner.Dispose();

        state.TryDequeueBodyChunk(out var c2);
        c2!.Owner.Dispose();

        Assert.Equal(0, state.PendingOutboundBytes);
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
