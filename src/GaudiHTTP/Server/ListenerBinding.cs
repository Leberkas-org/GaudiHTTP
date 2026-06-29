using Servus.Akka.Transport;

namespace GaudiHTTP.Server;

/// <summary>
/// Associates a set of listener configuration options with the factory that creates the
/// underlying transport listener (TCP or QUIC), and an optional structured-logging category
/// for per-connection log output.
/// </summary>
public sealed class ListenerBinding
{
    /// <summary>Gets the transport-level listener options (e.g. host, port, TLS settings).</summary>
    public required ListenerOptions Options { get; init; }
    /// <summary>Gets the factory used to instantiate the listener for these options.</summary>
    public required IListenerFactory Factory { get; init; }
    /// <summary>Gets the logger category name used for connection-level logging, or <c>null</c> to disable.</summary>
    public string? ConnectionLoggingCategory { get; init; }
    /// <summary>
    /// Gets the HTTP protocols this endpoint may negotiate. For cleartext endpoints this restricts
    /// protocol selection (e.g. an <see cref="HttpProtocols.Http1"/>-only endpoint rejects h2c);
    /// TLS endpoints additionally constrain negotiation via the advertised ALPN list. Defaults to
    /// HTTP/1.x + HTTP/2.
    /// </summary>
    public HttpProtocols Protocols { get; init; } = HttpProtocols.Http1AndHttp2;
}