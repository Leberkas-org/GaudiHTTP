namespace Servus.Akka.Transport.Tcp;

public interface ITcpConnectionFactory
{
    Task<ConnectionLease> EstablishAsync(TransportOptions options, CancellationToken ct);
}