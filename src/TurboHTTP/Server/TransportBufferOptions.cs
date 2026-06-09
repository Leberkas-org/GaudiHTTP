namespace TurboHTTP.Server;

/// <summary>
/// Controls backpressure thresholds on the read/write pipes between the OS socket
/// and the HTTP pipeline. These are applied per-connection for TCP and per-stream
/// for QUIC.
/// </summary>
public sealed class TransportBufferOptions
{
    /// <summary>
    /// The number of bytes buffered on the inbound (read) pipe before the writer
    /// pauses and signals backpressure to the OS. Default depends on the transport:
    /// TCP = 1 MiB (one pipe per connection), QUIC = 64 KiB (one pipe per stream).
    /// </summary>
    public long InputPauseThreshold { get; set; }

    /// <summary>
    /// The buffered byte count at which the inbound pipe resumes accepting data
    /// after a pause. Should be less than <see cref="InputPauseThreshold"/>.
    /// Default: TCP = 512 KiB, QUIC = 32 KiB.
    /// </summary>
    public long InputResumeThreshold { get; set; }

    /// <summary>
    /// The number of bytes buffered on the outbound (write) pipe before the writer
    /// pauses and signals backpressure to the HTTP pipeline. Default: 64 KiB.
    /// </summary>
    public long OutputPauseThreshold { get; set; } = 64 * 1024;

    /// <summary>
    /// The buffered byte count at which the outbound pipe resumes after a pause.
    /// Default: 32 KiB.
    /// </summary>
    public long OutputResumeThreshold { get; set; } = 32 * 1024;

    /// <summary>
    /// The minimum size of each buffer segment allocated by the pipe's memory pool.
    /// Larger values reduce segment count but increase per-pipe memory.
    /// Default: TCP = 16 KiB, QUIC = 4 KiB (one pipe per stream).
    /// </summary>
    public int MinimumSegmentSize { get; set; } = 16 * 1024;

    internal static TransportBufferOptions TcpDefaults => new()
    {
        InputPauseThreshold = 1024 * 1024,
        InputResumeThreshold = 512 * 1024,
        OutputPauseThreshold = 64 * 1024,
        OutputResumeThreshold = 32 * 1024,
        MinimumSegmentSize = 16 * 1024
    };

    internal static TransportBufferOptions QuicDefaults => new()
    {
        InputPauseThreshold = 64 * 1024,
        InputResumeThreshold = 32 * 1024,
        OutputPauseThreshold = 64 * 1024,
        OutputResumeThreshold = 32 * 1024,
        MinimumSegmentSize = 4 * 1024
    };
}
