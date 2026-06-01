namespace TurboHTTP.Server;

/// <summary>
/// HTTP/1.x-specific server configuration. Settings here override the corresponding values
/// in <see cref="TurboServerLimits"/> for HTTP/1.x connections; <c>null</c> means "inherit from limits".
/// </summary>
public sealed class Http1ServerOptions
{
    /// <summary>Gets or sets the maximum length of the HTTP request line (method + target + version). Default is 8 KiB.</summary>
    public int MaxRequestLineLength { get; set; } = 8 * 1024;
    /// <summary>Gets or sets the maximum length of the request-target (URL path + query). Default is 8 KiB.</summary>
    public int MaxRequestTargetLength { get; set; } = 8 * 1024;
    /// <summary>Gets or sets the maximum number of pipelined requests buffered per keep-alive connection. Default is 16.</summary>
    public int MaxPipelinedRequests { get; set; } = 16;
    /// <summary>Gets or sets the maximum length of chunked-encoding extensions per chunk. Default is 4 KiB.</summary>
    public int MaxChunkExtensionLength { get; set; } = 4 * 1024;
    /// <summary>Gets or sets the timeout for reading the complete request body after headers are received. Default is 30 seconds.</summary>
    public TimeSpan BodyReadTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the maximum total size of all request headers in bytes, or <c>null</c> to inherit from <see cref="TurboServerLimits.MaxRequestHeadersTotalSize"/>.</summary>
    public int? MaxHeaderListSize { get; set; }
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