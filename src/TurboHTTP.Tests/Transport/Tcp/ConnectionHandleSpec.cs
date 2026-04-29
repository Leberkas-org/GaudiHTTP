using System.Threading.Channels;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;

namespace TurboHTTP.Tests.Transport.Tcp;

public sealed class ConnectionHandleSpec
{
    [Fact(Timeout = 5000)]
    public void Write_should_push_buffer_to_outbound_channel()
    {
        var outbound = Channel.CreateUnbounded<TransportBuffer>();
        var inbound = Channel.CreateUnbounded<TransportBuffer>();
        var handle = new ConnectionHandle(outbound.Writer, inbound.Reader, CancellationToken.None);

        var buffer = TransportBuffer.Rent(16);
        buffer.Length = 4;
        handle.Write(buffer);

        Assert.True(outbound.Reader.TryRead(out var read));
        Assert.Equal(4, read!.Length);
        read.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void TryRead_should_return_buffer_from_inbound_channel()
    {
        var outbound = Channel.CreateUnbounded<TransportBuffer>();
        var inbound = Channel.CreateUnbounded<TransportBuffer>();
        var handle = new ConnectionHandle(outbound.Writer, inbound.Reader, CancellationToken.None);

        var buffer = TransportBuffer.Rent(16);
        buffer.Length = 8;
        inbound.Writer.TryWrite(buffer);

        Assert.True(handle.TryRead(out var read));
        Assert.Equal(8, read!.Length);
        read.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void SignalClose_should_complete_outbound_channel()
    {
        var outbound = Channel.CreateUnbounded<TransportBuffer>();
        var inbound = Channel.CreateUnbounded<TransportBuffer>();
        var handle = new ConnectionHandle(outbound.Writer, inbound.Reader, CancellationToken.None);

        handle.SignalClose();

        Assert.False(outbound.Writer.TryWrite(TransportBuffer.Rent(1)));
    }

    [Fact(Timeout = 5000)]
    public void IsCancelled_should_reflect_cancellation_token()
    {
        var outbound = Channel.CreateUnbounded<TransportBuffer>();
        var inbound = Channel.CreateUnbounded<TransportBuffer>();
        var cts = new CancellationTokenSource();
        var handle = new ConnectionHandle(outbound.Writer, inbound.Reader, cts.Token);

        Assert.False(handle.IsCancelled);
        cts.Cancel();
        Assert.True(handle.IsCancelled);
        cts.Dispose();
    }
}
