namespace TurboHTTP.Server;

public sealed class TurboServerLimits
{
    public int MaxConcurrentConnections { get; set; }
    public int MaxConcurrentUpgradedConnections { get; set; }
    public int MaxConcurrentRequests { get; set; }
    public int MinRequestGuarantee { get; set; } = 10;
    public long MaxRequestBodySize { get; set; } = 30 * 1024 * 1024;
    public int MaxRequestHeaderCount { get; set; } = 100;
    public int MaxRequestHeadersTotalSize { get; set; } = 32 * 1024;
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(130);
    public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public double MinRequestBodyDataRate { get; set; } = 240;
    public TimeSpan MinRequestBodyDataRateGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
    public double MinResponseDataRate { get; set; } = 240;
    public TimeSpan MinResponseDataRateGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
}
