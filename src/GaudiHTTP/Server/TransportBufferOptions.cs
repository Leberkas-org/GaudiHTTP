namespace GaudiHTTP.Server;

/// <summary>
/// Controls backpressure thresholds on the read/write pipes between the OS socket
/// and the HTTP pipeline. These are applied per-connection for TCP and per-stream
/// for QUIC. Properties left at <c>null</c> fall back to the protocol-specific
/// default (TCP buffers one pipe per connection, QUIC one pipe per stream).
/// </summary>
public sealed class TransportBufferOptions
{
    /// <summary>
    /// No longer has any effect. Inbound backpressure is now watermark-based inside
    /// servus.akka's unified rent-and-receive transport (<c>WireBuffer</c> + channel outbound);
    /// there is no separate inbound pipe pause/resume threshold to configure.
    /// </summary>
    [Obsolete("No longer has any effect. Inbound backpressure is watermark-based in the rent-and-receive transport and is no longer independently configurable.")]
    public long? InputPauseThreshold { get; set; }

    /// <summary>
    /// No longer has any effect. See <see cref="InputPauseThreshold"/>.
    /// </summary>
    [Obsolete("No longer has any effect. Inbound backpressure is watermark-based in the rent-and-receive transport and is no longer independently configurable.")]
    public long? InputResumeThreshold { get; set; }

    /// <summary>
    /// The number of bytes buffered on the outbound (write) pipe before the writer
    /// pauses and signals backpressure to the HTTP pipeline.
    /// <c>null</c> uses the transport default of 64 KiB.
    /// </summary>
    public long? OutputPauseThreshold { get; set; }

    /// <summary>
    /// The buffered byte count at which the outbound pipe resumes after a pause.
    /// Must be less than or equal to <see cref="OutputPauseThreshold"/>.
    /// <c>null</c> uses the transport default of 32 KiB.
    /// </summary>
    public long? OutputResumeThreshold { get; set; }

    /// <summary>
    /// No longer has any effect. Buffer segment sizing is now internal to <c>WireBuffer</c>'s
    /// shared pool in servus.akka and is not independently configurable per listener.
    /// </summary>
    [Obsolete("No longer has any effect. Buffer segment sizing is internal to WireBuffer's shared pool and is no longer independently configurable.")]
    public int? MinimumSegmentSize { get; set; }

    /// <summary>
    /// Size hint passed to the receive path. Controls the minimum buffer segment actually
    /// allocated per read. <c>null</c> uses the transport
    /// default: TCP = 64 KiB (long-lived connection), QUIC = 4 KiB (short-lived stream).
    /// </summary>
    public int? ReceiveBufferHint { get; set; }

    internal ResolvedTransportBuffers ResolveTcp() => Resolve(defaultReceiveBufferHint: 64 * 1024);

    internal ResolvedTransportBuffers ResolveQuic() => Resolve(defaultReceiveBufferHint: 4 * 1024);

    internal static ResolvedTransportBuffers TcpDefaults { get; } = new TransportBufferOptions().ResolveTcp();

    internal static ResolvedTransportBuffers QuicDefaults { get; } = new TransportBufferOptions().ResolveQuic();

    private ResolvedTransportBuffers Resolve(int defaultReceiveBufferHint)
    {
        var resolved = new ResolvedTransportBuffers(
            OutputPauseThreshold: OutputPauseThreshold ?? 64 * 1024,
            OutputResumeThreshold: OutputResumeThreshold ?? 32 * 1024,
            ReceiveBufferHint: ReceiveBufferHint ?? defaultReceiveBufferHint);

        if (resolved.OutputResumeThreshold > resolved.OutputPauseThreshold)
        {
            throw new InvalidOperationException(
                string.Concat(
                    "TransportBufferOptions: OutputResumeThreshold (", resolved.OutputResumeThreshold.ToString(),
                    ") must not exceed OutputPauseThreshold (", resolved.OutputPauseThreshold.ToString(), ")."));
        }

        return resolved;
    }
}

/// <summary>
/// Transport buffer thresholds with all defaults applied, ready to project onto listener options.
/// </summary>
internal readonly record struct ResolvedTransportBuffers(
    long OutputPauseThreshold,
    long OutputResumeThreshold,
    int ReceiveBufferHint);
