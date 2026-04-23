namespace Servus.Akka.IO;

public interface IConnectionFactory
{
    Task<ConnectionLease> EstablishAsync(ITransportOptions options, RequestEndpoint endpoint, CancellationToken ct);
}
