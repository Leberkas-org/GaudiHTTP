namespace TurboHTTP.Server;

/// <summary>
/// HTTP/2-specific server configuration.
/// Controls stream concurrency, flow-control windows, HPACK table size, frame size, response buffering,
/// and keep-alive pings. Nullable properties inherit from <see cref="TurboServerLimits"/>
/// when left at <c>null</c>.
/// </summary>
public sealed class Http2ServerOptions
{
    /// <summary>Gets or sets the maximum number of concurrent streams per HTTP/2 connection. Default is 100.</summary>
    public int MaxConcurrentStreams { get; set; } = 100;
    /// <summary>Gets or sets the initial HTTP/2 connection-level flow-control window size in bytes. Default is 1 MiB.</summary>
    public int InitialConnectionWindowSize { get; set; } = 1 * 1024 * 1024;
    /// <summary>Gets or sets the initial HTTP/2 stream-level flow-control window size in bytes. Default is 768 KiB.</summary>
    public int InitialStreamWindowSize { get; set; } = 768 * 1024;
    /// <summary>Upper bound the per-stream receive window may grow to under adaptive scaling, in bytes. Default is 8 MiB.</summary>
    public int MaxStreamWindowSize { get; set; } = 8 * 1024 * 1024;
    /// <summary>Threshold multiplier for adaptive window growth. Higher values grow the window less eagerly. Default is 1.0.</summary>
    public double WindowScaleThresholdMultiplier { get; set; } = 1.0;
    /// <summary>Enables server-side adaptive (BDP-based) receive-window scaling. When true, the per-stream receive window grows from <see cref="InitialStreamWindowSize"/> up to <see cref="MaxStreamWindowSize"/> based on measured throughput and RTT. Default is true.</summary>
    public bool EnableAdaptiveWindowScaling { get; set; } = true;
    /// <summary>Gets or sets the maximum HTTP/2 frame size in bytes. Default is 16 KiB.</summary>
    public int MaxFrameSize { get; set; } = 16 * 1024;
    /// <summary>Gets or sets the HPACK dynamic header table size in bytes. Default is 4 KiB.</summary>
    public int HeaderTableSize { get; set; } = 4 * 1024;
    /// <summary>Gets or sets the maximum total size of request headers in bytes, or <c>null</c> to inherit from <see cref="TurboServerLimits.MaxRequestHeadersTotalSize"/>.</summary>
    public int? MaxHeaderListSize { get; set; }
    /// <summary>Gets or sets the maximum size of the response write buffer in bytes, or <c>null</c> to inherit from <see cref="TurboServerLimits.MaxResponseBufferSize"/>.</summary>
    public long? MaxResponseBufferSize { get; set; }
    /// <summary>Gets or sets the maximum allowed request body size in bytes, or <c>null</c> to inherit from <see cref="TurboServerLimits.MaxRequestBodySize"/>.</summary>
    public long? MaxRequestBodySize { get; set; }
    /// <summary>Gets or sets the keep-alive idle timeout, or <c>null</c> to inherit from <see cref="TurboServerLimits.KeepAliveTimeout"/>.</summary>
    public TimeSpan? KeepAliveTimeout { get; set; }
    /// <summary>Gets or sets the timeout for receiving the complete request headers, or <c>null</c> to inherit from <see cref="TurboServerLimits.RequestHeadersTimeout"/>.</summary>
    public TimeSpan? RequestHeadersTimeout { get; set; }
    /// <summary>Gets or sets the minimum acceptable request body data rate in bytes/second, or <c>null</c> to inherit from <see cref="TurboServerLimits.MinRequestBodyDataRate"/>.</summary>
    public double? MinRequestBodyDataRate { get; set; }
    /// <summary>Gets or sets the grace period before enforcing the minimum request body data rate, or <c>null</c> to inherit from <see cref="TurboServerLimits.MinRequestBodyDataRateGracePeriod"/>.</summary>
    public TimeSpan? MinRequestBodyDataRateGracePeriod { get; set; }
    /// <summary>Gets or sets the minimum acceptable response data rate in bytes/second, or <c>null</c> to inherit from <see cref="TurboServerLimits.MinResponseDataRate"/>.</summary>
    public double? MinResponseDataRate { get; set; }
    /// <summary>Gets or sets the grace period before enforcing the minimum response data rate, or <c>null</c> to inherit from <see cref="TurboServerLimits.MinResponseDataRateGracePeriod"/>.</summary>
    public TimeSpan? MinResponseDataRateGracePeriod { get; set; }

    /// <summary>
    /// Idle time after receiving the last frame before the server sends a keep-alive PING to detect dead connections.
    /// Set to <see cref="Timeout.InfiniteTimeSpan"/> to disable server-initiated keep-alive pings (default).
    /// </summary>
    public TimeSpan KeepAlivePingDelay { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Maximum time to wait for a PING ACK before closing the connection. Default is 20 seconds.
    /// </summary>
    public TimeSpan KeepAlivePingTimeout { get; set; } = TimeSpan.FromSeconds(20);
}