namespace Servus.Akka.Transport;

public interface IConnectionFactory<TLease>
{
    Task<TLease> EstablishAsync(TransportOptions options, CancellationToken ct);
}