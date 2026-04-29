namespace Servus.Akka.Transport.Quic;

internal interface IQuicConnectionFactory
{
    Task<QuicConnectionLease> EstablishAsync(QuicTransportOptions options, CancellationToken ct);
}
