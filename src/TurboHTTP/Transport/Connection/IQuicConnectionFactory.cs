using TurboHTTP.Internal;

namespace TurboHTTP.Transport.Connection;

internal interface IQuicConnectionFactory
{
    Task<QuicConnectionLease> EstablishAsync(QuicOptions options, RequestEndpoint endpoint, CancellationToken ct);
}
