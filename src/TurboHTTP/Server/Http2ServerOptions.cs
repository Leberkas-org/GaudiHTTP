namespace TurboHTTP.Server;

public sealed class Http2ServerOptions
{
    public int MaxConcurrentStreams { get; set; } = 100;
    public int InitialConnectionWindowSize { get; set; } = 1 * 1024 * 1024;
    public int InitialStreamWindowSize { get; set; } = 768 * 1024;
    public int MaxFrameSize { get; set; } = 16 * 1024;
    public int HeaderTableSize { get; set; } = 4 * 1024;
    public int? MaxHeaderListSize { get; set; }
    public long MaxResponseBufferSize { get; set; } = 64 * 1024;
    public long? MaxRequestBodySize { get; set; }
    public TimeSpan? KeepAliveTimeout { get; set; }
    public TimeSpan? RequestHeadersTimeout { get; set; }
    public double? MinRequestBodyDataRate { get; set; }
    public TimeSpan? MinRequestBodyDataRateGracePeriod { get; set; }
    public double? MinResponseDataRate { get; set; }
    public TimeSpan? MinResponseDataRateGracePeriod { get; set; }
}