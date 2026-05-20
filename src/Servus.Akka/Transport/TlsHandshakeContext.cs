using System.Net;
using System.Net.Security;

namespace Servus.Akka.Transport;

public sealed class TlsHandshakeContext(
    SslStream sslStream,
    SslClientHelloInfo clientHelloInfo,
    EndPoint localEndPoint,
    EndPoint remoteEndPoint,
    CancellationToken cancellationToken)
{
    public SslStream SslStream { get; } = sslStream;
    public SslClientHelloInfo ClientHelloInfo { get; } = clientHelloInfo;
    public EndPoint LocalEndPoint { get; } = localEndPoint;
    public EndPoint RemoteEndPoint { get; } = remoteEndPoint;
    public CancellationToken CancellationToken { get; } = cancellationToken;
    public bool AllowDelayedClientCertificateNegotiation { get; set; }
}
