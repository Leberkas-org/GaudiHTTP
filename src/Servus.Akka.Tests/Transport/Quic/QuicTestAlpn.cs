using System.Net.Security;

namespace Servus.Akka.Tests.Transport.Quic;

internal static class QuicTestAlpn
{
    // Use "h3" (HTTP/3) as ALPN for testing - Windows QUIC may only support standard ALPN protocols
    public static readonly SslApplicationProtocol Instance = new SslApplicationProtocol("h3");
}
