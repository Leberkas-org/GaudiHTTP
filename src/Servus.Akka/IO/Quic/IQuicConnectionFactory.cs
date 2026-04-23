namespace Servus.Akka.IO.Quic;

public interface IQuicConnectionFactory
{
    Task<QuicConnectionLease> EstablishAsync(QuicOptions options, RequestEndpoint endpoint, CancellationToken ct);
}
