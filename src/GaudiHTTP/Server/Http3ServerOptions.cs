namespace GaudiHTTP.Server;

/// <summary>
/// HTTP/3-specific server configuration.
/// Controls stream concurrency, QPACK compression, response buffering, and data-rate enforcement.
/// Nullable properties inherit from <see cref="TurboServerLimits"/>
/// when left at <c>null</c>.
/// </summary>
public sealed class Http3ServerOptions
{
    /// <summary>Gets or sets the maximum number of concurrent streams per HTTP/3 connection. Default is 100.</summary>
    public int MaxConcurrentStreams { get; set; } = 100;

    /// <summary>Gets or sets the maximum total size of request headers in bytes, or <c>null</c> to inherit from <see cref="TurboServerLimits.MaxRequestHeadersTotalSize"/>.</summary>
    public int? MaxHeaderListSize { get; set; }

    /// <summary>Gets or sets the QPACK dynamic table capacity in bytes. Default is 0 (dynamic table disabled).</summary>
    public int QpackMaxTableCapacity { get; set; }

    /// <summary>Gets or sets the maximum number of blocked streams waiting for QPACK decoder instructions. Default is 100.</summary>
    public int QpackBlockedStreams { get; set; } = 100;

    /// <summary>Gets or sets the maximum size of the per-stream response write buffer in bytes, or <c>null</c> to inherit from <see cref="TurboServerLimits.MaxResponseBufferSize"/>.</summary>
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
}