namespace TurboHTTP.Server;

/// <summary>
/// Server-wide limits applied to all connections and protocols. Individual protocol options
/// (<see cref="Http1ServerOptions"/>, <see cref="Http2ServerOptions"/>, <see cref="Http3ServerOptions"/>)
/// can override these values per protocol; <c>null</c> overrides fall back to the values here.
/// </summary>
public sealed class TurboServerLimits
{
    /// <summary>Gets or sets the maximum number of concurrent connections the server accepts. Default is 0 (unlimited).</summary>
    public int MaxConcurrentConnections { get; set; }
    /// <summary>Gets or sets the maximum number of requests processed concurrently across all connections. Default is 0 (unlimited).</summary>
    public int MaxConcurrentRequests { get; set; }
    /// <summary>Gets or sets the minimum number of concurrent requests guaranteed per connection even under load. Default is 10.</summary>
    public int MinRequestGuarantee { get; set; } = 10;
    /// <summary>Gets or sets the default maximum request body size in bytes for all protocols. Default is 30 MiB.</summary>
    public long MaxRequestBodySize { get; set; } = 30 * 1024 * 1024;
    /// <summary>Gets or sets the maximum number of headers allowed in a single request. Default is 100.</summary>
    public int MaxRequestHeaderCount { get; set; } = 100;
    /// <summary>Gets or sets the maximum combined size in bytes of all request headers. Default is 32 KiB.</summary>
    public int MaxRequestHeadersTotalSize { get; set; } = 32 * 1024;

    /// <summary>
    /// HTTP/2 Rapid Reset (CVE-2023-44487) mitigation: the maximum number of client-initiated stream
    /// resets tolerated within a sliding window before the connection is closed with
    /// GOAWAY(ENHANCE_YOUR_CALM). Aligned with Kestrel's default. Set to 0 to disable the mitigation.
    /// </summary>
    public int MaxResetStreamsPerWindow { get; set; } = 200;

    /// <summary>Gets or sets the keep-alive idle timeout for HTTP/1.x and HTTP/2 connections. Default is 130 seconds.</summary>
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(130);
    /// <summary>Gets or sets the maximum time to receive the complete request headers after the connection is accepted. Default is 30 seconds.</summary>
    public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the minimum acceptable request body data rate in bytes/second. Default is 240 bytes/second.</summary>
    public double MinRequestBodyDataRate { get; set; } = 240;
    /// <summary>Gets or sets the grace period before the minimum request body data rate is enforced. Default is 5 seconds.</summary>
    public TimeSpan MinRequestBodyDataRateGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets or sets the minimum acceptable response data rate in bytes/second. Default is 240 bytes/second.</summary>
    public double MinResponseDataRate { get; set; } = 240;
    /// <summary>Gets or sets the grace period before the minimum response data rate is enforced. Default is 5 seconds.</summary>
    public TimeSpan MinResponseDataRateGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
}
