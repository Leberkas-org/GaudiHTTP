namespace TurboHTTP.Server;

public sealed class Http3ServerOptions
{
    public int MaxConcurrentStreams { get; set; } = 100;
    public int? MaxHeaderListSize { get; set; }
    public int QpackMaxTableCapacity { get; set; }
    public int QpackBlockedStreams { get; set; } = 100;
    public long? MaxRequestBodySize { get; set; }
    public TimeSpan? KeepAliveTimeout { get; set; }
    public TimeSpan? RequestHeadersTimeout { get; set; }
    public double? MinRequestBodyDataRate { get; set; }
    public TimeSpan? MinRequestBodyDataRateGracePeriod { get; set; }
    public double? MinResponseDataRate { get; set; }
    public TimeSpan? MinResponseDataRateGracePeriod { get; set; }
}