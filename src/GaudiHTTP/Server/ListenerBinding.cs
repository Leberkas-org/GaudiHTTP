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
}