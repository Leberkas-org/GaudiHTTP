using System.Net.Security;
using System.Security.Authentication;

namespace TurboHTTP.Server.Context.Features;

/// <summary>
/// Exposes TLS handshake details for a connection: the negotiated protocol version,
/// cipher suite, SNI host name, and ALPN application protocol.
/// </summary>
public interface ITlsHandshakeFeature
{
    /// <summary>Gets the TLS protocol version negotiated during the handshake.</summary>
    SslProtocols Protocol { get; }
    /// <summary>Gets the cipher suite negotiated during the handshake, or <c>null</c> if unavailable.</summary>
    TlsCipherSuite? NegotiatedCipherSuite { get; }
    /// <summary>Gets the SNI host name provided by the client, or <c>null</c> if not supplied.</summary>
    string? HostName { get; }
    /// <summary>Gets the ALPN application protocol negotiated during the handshake (e.g. "h2" or "h3").</summary>
    SslApplicationProtocol NegotiatedApplicationProtocol { get; }
}
