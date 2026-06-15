namespace TurboHTTP.Server;

/// <summary>
/// Controls backpressure thresholds on the read/write pipes between the OS socket
/// and the HTTP pipeline. These are applied per-connection for TCP and per-stream
/// for QUIC. Properties left at <c>null</c> fall back to the protocol-specific
/// default (TCP buffers one pipe per connection, QUIC one pipe per stream).
/// </summary>
public sealed class TransportBufferOptions
{
    /// <summary>
    /// The number of bytes buffered on the inbound (read) pipe before the writer
    /// pauses and signals backpressure to the OS. <c>null</c> uses the transport
    /// default: TCP = 1 MiB (one pipe per connection), QUIC = 64 KiB (one pipe per stream).
    /// </summary>
    public long? InputPauseThreshold { get; set; }

    /// <summary>
    /// The buffered byte count at which the inbound pipe resumes accepting data
    /// after a pause. Must be less than or equal to <see cref="InputPauseThreshold"/>.
    /// <c>null</c> uses the transport default: TCP = 512 KiB, QUIC = 32 KiB.
    /// </summary>
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
    /// The minimum size of each buffer segment allocated by the pipe's memory pool.
    /// Larger values reduce segment count but increase per-pipe memory.
    /// <c>null</c> uses the transport default: TCP = 16 KiB, QUIC = 4 KiB (one pipe per stream).
    /// </summary>
    public int? MinimumSegmentSize { get; set; }

    /// <summary>
    /// Size hint passed to PipeWriter.GetMemory on the receive path. Controls the
    /// minimum buffer segment actually allocated per read. <c>null</c> uses the transport
    /// default: TCP = 64 KiB (long-lived connection), QUIC = 4 KiB (short-lived stream).
    /// </summary>
    public int? ReceiveBufferHint { get; set; }

    internal ResolvedTransportBuffers ResolveTcp() => Resolve(
        defaultInputPause: 1024 * 1024,
        defaultInputResume: 512 * 1024,
        defaultMinimumSegmentSize: 16 * 1024,
        defaultReceiveBufferHint: 64 * 1024);

    internal ResolvedTransportBuffers ResolveQuic() => Resolve(
        defaultInputPause: 64 * 1024,
        defaultInputResume: 32 * 1024,
        defaultMinimumSegmentSize: 4 * 1024,
        defaultReceiveBufferHint: 4 * 1024);

    internal static ResolvedTransportBuffers TcpDefaults { get; } = new TransportBufferOptions().ResolveTcp();

    internal static ResolvedTransportBuffers QuicDefaults { get; } = new TransportBufferOptions().ResolveQuic();

    private ResolvedTransportBuffers Resolve(long defaultInputPause, long defaultInputResume,
        int defaultMinimumSegmentSize, int defaultReceiveBufferHint)
    {
        var resolved = new ResolvedTransportBuffers(
            InputPauseThreshold: InputPauseThreshold ?? defaultInputPause,
            InputResumeThreshold: InputResumeThreshold ?? defaultInputResume,
            OutputPauseThreshold: OutputPauseThreshold ?? 64 * 1024,
            OutputResumeThreshold: OutputResumeThreshold ?? 32 * 1024,
            MinimumSegmentSize: MinimumSegmentSize ?? defaultMinimumSegmentSize,
            ReceiveBufferHint: ReceiveBufferHint ?? defaultReceiveBufferHint);

        if (resolved.InputResumeThreshold > resolved.InputPauseThreshold)
        {
            throw new InvalidOperationException(
                string.Concat(
                    "TransportBufferOptions: InputResumeThreshold (", resolved.InputResumeThreshold.ToString(),
                    ") must not exceed InputPauseThreshold (", resolved.InputPauseThreshold.ToString(), ")."));
        }

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
    long InputPauseThreshold,
    long InputResumeThreshold,
    long OutputPauseThreshold,
    long OutputResumeThreshold,
    int MinimumSegmentSize,
    int ReceiveBufferHint);
