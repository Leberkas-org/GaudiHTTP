namespace TurboHTTP.Server;

public sealed class Http1ServerOptions
{
    public int MaxRequestLineLength { get; set; } = 8 * 1024;
    public int MaxRequestTargetLength { get; set; } = 8 * 1024;
    public int MaxPipelinedRequests { get; set; } = 16;
    public int MaxChunkExtensionLength { get; set; } = 4 * 1024;
    public TimeSpan BodyReadTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxHeaderListSize { get; set; } = 32 * 1024;
    public long? MaxRequestBodySize { get; set; }
    public TimeSpan? KeepAliveTimeout { get; set; }
    public TimeSpan? RequestHeadersTimeout { get; set; }
    public double? MinRequestBodyDataRate { get; set; }
    public TimeSpan? MinRequestBodyDataRateGracePeriod { get; set; }
    public double? MinResponseDataRate { get; set; }
    public TimeSpan? MinResponseDataRateGracePeriod { get; set; }
}