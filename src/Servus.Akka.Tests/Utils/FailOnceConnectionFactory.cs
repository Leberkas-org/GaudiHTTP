using System.Threading.Channels;
using Servus.Akka.IO;

namespace Servus.Akka.Tests.Utils;

/// <summary>
/// A test factory that fails the first EstablishAsync call, then succeeds on all subsequent calls.
/// Used to verify that connection-establishment failures trigger ServeNextPending cascades.
/// </summary>
internal sealed class FailOnceConnectionFactory : IConnectionFactory
{
    private int _callCount;

    public Task<ConnectionLease> EstablishAsync(ITransportOptions options, RequestEndpoint endpoint, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (Interlocked.Increment(ref _callCount) == 1)
        {
            return Task.FromException<ConnectionLease>(new IOException("Simulated first-call connection failure"));
        }

        var state = new ClientState(Stream.Null);
        var handle = ConnectionHandle.CreateDirect(state.OutboundWriter, state.InboundReader, endpoint);
        return Task.FromResult(new ConnectionLease(handle, state));
    }
}
