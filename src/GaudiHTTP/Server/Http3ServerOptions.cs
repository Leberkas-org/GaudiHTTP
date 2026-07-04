namespace GaudiHTTP.Server;

/// <summary>
/// HTTP/3-specific server configuration.
/// Controls stream concurrency, QPACK compression, response buffering, and data-rate enforcement.
/// Nullable properties inherit from <see cref="GaudiServerLimits"/>
/// when left at <c>null</c>.
/// </summary>
public sealed class Http3ServerOptions
{
    /// <summary>Gets or sets the maximum number of concurrent streams per HTTP/3 connection. Default is 100.</summary>
    public int MaxConcurrentStreams { get; set; } = 100;

    /// <summary>Gets or sets the maximum total size of request headers in bytes, or <c>null</c> to inherit from <see cref="GaudiServerLimits.MaxRequestHeadersTotalSize"/>.</summary>
    public int? MaxHeaderListSize { get; set; }

    /// <summary>Gets or sets the QPACK dynamic table capacity in bytes. Default is 0 (dynamic table disabled).</summary>
    public int QpackMaxTableCapacity { get; set; }

    /// <summary>Gets or sets the maximum number of blocked streams waiting for QPACK decoder instructions. Default is 100.</summary>
    public int QpackBlockedStreams { get; set; } = 100;

    /// <summary>Gets or sets the maximum allowed request body size in bytes, or <c>null</c> to inherit from <see cref="GaudiServerLimits.MaxRequestBodySize"/>.</summary>
    public long? MaxRequestBodySize { get; set; }

    /// <summary>Gets or sets the keep-alive idle timeout, or <c>null</c> to inherit from <see cref="GaudiServerLimits.KeepAliveTimeout"/>.</summary>
    public TimeSpan? KeepAliveTimeout { get; set; }

    /// <summary>Gets or sets the timeout for receiving the complete request headers, or <c>null</c> to inherit from <see cref="GaudiServerLimits.RequestHeadersTimeout"/>.</summary>
    public TimeSpan? RequestHeadersTimeout { get; set; }

    /// <summary>Gets or sets the minimum acceptable request body data rate in bytes/second, or <c>null</c> to inherit from <see cref="GaudiServerLimits.MinRequestBodyDataRate"/>.</summary>
    public double? MinRequestBodyDataRate { get; set; }

    /// <summary>Gets or sets the grace period before enforcing the minimum request body data rate, or <c>null</c> to inherit from <see cref="GaudiServerLimits.MinRequestBodyDataRateGracePeriod"/>.</summary>
    public TimeSpan? MinRequestBodyDataRateGracePeriod { get; set; }

    /// <summary>Gets or sets the minimum acceptable response data rate in bytes/second, or <c>null</c> to inherit from <see cref="GaudiServerLimits.MinResponseDataRate"/>.</summary>
    public double? MinResponseDataRate { get; set; }

    /// <summary>Gets or sets the grace period before enforcing the minimum response data rate, or <c>null</c> to inherit from <see cref="GaudiServerLimits.MinResponseDataRateGracePeriod"/>.</summary>
    public TimeSpan? MinResponseDataRateGracePeriod { get; set; }

    /// <summary>
    /// Per-protocol override for the maximum request body size (in bytes) that is buffered fully
    /// in memory. When <see langword="null"/>, inherits from
    /// <see cref="GaudiServerOptions.MaxBufferedRequestBodySize"/> then
    /// <see cref="GaudiServerOptions.MaxBufferedBodySize"/>. Default is <see langword="null"/>.
    /// </summary>
    public int? MaxBufferedRequestBodySize { get; set; }

    /// <summary>
    /// Per-protocol override for the maximum response body size (in bytes) that is buffered fully
    /// in memory. When <see langword="null"/>, inherits from
    /// <see cref="GaudiServerOptions.MaxBufferedResponseBodySize"/> then
    /// <see cref="GaudiServerOptions.MaxBufferedBodySize"/>. Default is <see langword="null"/>.
    /// </summary>
    public int? MaxBufferedResponseBodySize { get; set; }

    /// <summary>
    /// Per-protocol override for response body chunk size (in bytes). When <see langword="null"/>,
    /// inherits from <see cref="GaudiServerOptions.ResponseBodyChunkSize"/> then
    /// <see cref="GaudiServerOptions.BodyChunkSize"/>. Default is <see langword="null"/>.
    /// </summary>
    public int? ResponseBodyChunkSize { get; set; }
}