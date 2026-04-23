using System.Threading.Channels;
using Servus.Akka.IO;

namespace Servus.Akka.Tests.Utils;

internal sealed class InMemoryConnectionFactory : IConnectionFactory
{
    private readonly List<ConnectionLease> _established = [];

    public IReadOnlyList<ConnectionLease> EstablishedLeases => _established;

    public Task<ConnectionLease> EstablishAsync(ITransportOptions options, RequestEndpoint endpoint, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var inbound = Channel.CreateUnbounded<NetworkBuffer>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        var outbound = Channel.CreateUnbounded<NetworkBuffer>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        var handle = ConnectionHandle.CreateDirect(
            outbound.Writer,
            inbound.Reader,
            endpoint);

        var state = new ClientState(
            Stream.Null,
            inbound,
            outbound);

        var lease = new ConnectionLease(handle, state);
        _established.Add(lease);
        return Task.FromResult(lease);
    }
}
