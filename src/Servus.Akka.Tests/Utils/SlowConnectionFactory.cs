using System.Threading.Channels;
using Servus.Akka.IO;

namespace Servus.Akka.Tests.Utils;

/// <summary>
/// A test factory that intentionally ignores the cancellation token during establish, simulating
/// a slow network that completes after the caller has already cancelled their request.
/// Used to exercise the OnEstablished path where TrySetResult returns false.
/// </summary>
internal sealed class SlowConnectionFactory(TimeSpan delay) : IConnectionFactory
{
    public async Task<ConnectionLease> EstablishAsync(ITransportOptions options, RequestEndpoint endpoint, CancellationToken ct)
    {
        // Deliberately ignore ct — simulates a slow network that doesn't respect cancellation.
        await Task.Delay(delay, CancellationToken.None).ConfigureAwait(false);

        var state = new ClientState(Stream.Null);
        var handle = ConnectionHandle.CreateDirect(state.OutboundWriter, state.InboundReader, endpoint);
        return new ConnectionLease(handle, state);
    }
}
