using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic;

namespace Servus.Akka.Tests.Transport.Quic;

[Collection("TransportBuffer")]
public sealed class StreamContextSpec
{
    [Fact(Timeout = 5000)]
    public void HasHandle_should_return_false_initially()
    {
        var ctx = new StreamContext(StreamDirection.Bidirectional);
        Assert.False(ctx.HasHandle());
    }

    [Fact(Timeout = 5000)]
    public void HasHandle_should_return_true_after_attach()
    {
        var ctx = new StreamContext(StreamDirection.Bidirectional);
        var handle = new StreamHandle(Stream.Null, null);
        ctx.AttachHandle(handle);
        Assert.True(ctx.HasHandle());
    }

    [Fact(Timeout = 5000)]
    public void Write_should_enqueue_when_no_handle()
    {
        var ctx = new StreamContext(StreamDirection.Bidirectional);
        var buffer = TransportBuffer.Rent(16);
        buffer.Length = 4;

        ctx.Write(buffer);

        Assert.True(ctx.TryDequeuePendingWrite(out var dequeued));
        Assert.Equal(4, dequeued!.Length);
        dequeued.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void TryDequeuePendingWrite_should_return_false_when_empty()
    {
        var ctx = new StreamContext(StreamDirection.Bidirectional);
        Assert.False(ctx.TryDequeuePendingWrite(out _));
    }

    [Fact(Timeout = 5000)]
    public void Direction_should_return_construction_value()
    {
        var ctx = new StreamContext(StreamDirection.Unidirectional);
        Assert.Equal(StreamDirection.Unidirectional, ctx.Direction());
    }
}
