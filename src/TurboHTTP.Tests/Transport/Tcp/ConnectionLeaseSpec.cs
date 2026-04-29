using System.Threading.Channels;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;

namespace TurboHTTP.Tests.Transport.Tcp;

public sealed class ConnectionLeaseSpec
{
    private static (ConnectionLease lease, ConnectionHandle handle) CreateLease()
    {
        var outbound = Channel.CreateUnbounded<TransportBuffer>();
        var inbound = Channel.CreateUnbounded<TransportBuffer>();
        var cts = new CancellationTokenSource();
        var handle = new ConnectionHandle(outbound.Writer, inbound.Reader, cts.Token);
        var state = new ClientState(Stream.Null, PipeMode.Bidirectional);
        var lease = new ConnectionLease(handle, state, cts);
        return (lease, handle);
    }

    [Fact(Timeout = 5000)]
    public void IsAlive_should_return_true_initially()
    {
        var (lease, _) = CreateLease();
        Assert.True(lease.IsAlive());
        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void IsAlive_should_return_false_after_dispose()
    {
        var (lease, _) = CreateLease();
        lease.Dispose();
        Assert.False(lease.IsAlive());
    }

    [Fact(Timeout = 5000)]
    public void IsExpired_should_return_false_for_infinite_lifetime()
    {
        var (lease, _) = CreateLease();
        Assert.False(lease.IsExpired(Timeout.InfiniteTimeSpan));
        lease.Dispose();
    }

    [Fact(Timeout = 5000)]
    public void Dispose_should_cancel_connection_handle()
    {
        var (lease, handle) = CreateLease();
        Assert.False(handle.IsCancelled);
        lease.Dispose();
        Assert.True(handle.IsCancelled);
    }
}
