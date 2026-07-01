namespace GaudiHTTP.Server;

/// <summary>
/// HTTP/1.x-specific server configuration.
/// Controls request line parsing, pipelining, chunked-encoding limits, body read timeouts,
/// and data-rate enforcement. Nullable properties inherit from <see cref="GaudiServerLimits"/>
/// when left at <c>null</c>.
/// <para>
/// Note: the connection timeouts (<see cref="RequestHeadersTimeout"/>, <see cref="BodyReadTimeout"/>,
/// <see cref="KeepAliveTimeout"/>, and the server-wide <c>BodyConsumptionTimeout</c>) are enforced for
/// HTTP/1.1 connections. Legacy HTTP/1.0 is close-per-request and is exempt from these timeouts; the
/// minimum data-rate limits, body-size limits, and header limits are still enforced for HTTP/1.0.
/// </para>
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

    /// <summary>
    /// Maximum length (in bytes) of a chunk-size control line in chunked transfer encoding.
    /// Guards against oversized chunk headers. Default is 64 KiB.
    /// </summary>
    public int MaxChunkedControlLineLength { get; set; } = 64 * 1024;

    /// <summary>
    /// Maximum total size (in bytes) of the trailer section in chunked transfer encoding.
    /// Guards against trailer bombs. Default is 32 KiB.
    /// </summary>
    public int MaxChunkedTrailerSize { get; set; } = 32 * 1024;

    /// <summary>
    /// Per-protocol override for the maximum request body size (in bytes) that is buffered fully
    /// in memory. Bodies larger than this are exposed as a streaming pipe with back-pressure.
    /// When <see langword="null"/>, inherits from <see cref="GaudiServerOptions.MaxBufferedRequestBodySize"/>
    /// then <see cref="GaudiServerOptions.MaxBufferedBodySize"/>. Default is <see langword="null"/>.
    /// </summary>
    public int? MaxBufferedRequestBodySize { get; set; }

    /// <summary>
    /// Per-protocol override for response body chunk size (in bytes). When <see langword="null"/>,
    /// inherits from <see cref="GaudiServerOptions.ResponseBodyChunkSize"/> then
    /// <see cref="GaudiServerOptions.BodyChunkSize"/>. Default is <see langword="null"/>.
    /// </summary>
    public int? ResponseBodyChunkSize { get; set; }

    /// <summary>Gets or sets the timeout for reading the complete request body after headers are received. Default is 30 seconds.</summary>
    public TimeSpan BodyReadTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the maximum total size of all request headers in bytes, or <c>null</c> to inherit from <see cref="GaudiServerLimits.MaxRequestHeadersTotalSize"/>.</summary>
    public int? MaxHeaderListSize { get; set; }

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
}