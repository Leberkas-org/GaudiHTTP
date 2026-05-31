namespace TurboHTTP.Server;

internal static class ServerOptionsProjections
{
    public static Http1ConnectionOptions ToHttp1Options(this TurboServerOptions o)
        => new()
        {
            Limits = ResolveLimits(o, o.Http1.MaxRequestBodySize, o.Http1.KeepAliveTimeout,
                o.Http1.RequestHeadersTimeout, o.Http1.MinRequestBodyDataRate,
                o.Http1.MinRequestBodyDataRateGracePeriod, o.Http1.MinResponseDataRate,
                o.Http1.MinResponseDataRateGracePeriod),
            MaxRequestLineLength = o.Http1.MaxRequestLineLength,
            MaxRequestTargetLength = o.Http1.MaxRequestTargetLength,
            MaxPipelinedRequests = o.Http1.MaxPipelinedRequests,
            MaxChunkExtensionLength = o.Http1.MaxChunkExtensionLength,
            MaxHeaderListSize = o.Http1.MaxHeaderListSize ?? o.Limits.MaxRequestHeadersTotalSize,
            MaxHeaderCount = o.Limits.MaxRequestHeaderCount,
            AllowObsFold = false,
            BodyReadTimeout = o.Http1.BodyReadTimeout,
            BodyBufferThreshold = o.BodyBufferThreshold,
            ResponseBodyChunkSize = o.ResponseBodyChunkSize,
            BodyConsumptionTimeout = o.BodyConsumptionTimeout,
        };

    public static Http2ConnectionOptions ToHttp2Options(this TurboServerOptions o)
        => new()
        {
            Limits = ResolveLimits(o, o.Http2.MaxRequestBodySize, o.Http2.KeepAliveTimeout,
                o.Http2.RequestHeadersTimeout, o.Http2.MinRequestBodyDataRate,
                o.Http2.MinRequestBodyDataRateGracePeriod, o.Http2.MinResponseDataRate,
                o.Http2.MinResponseDataRateGracePeriod),
            MaxConcurrentStreams = o.Http2.MaxConcurrentStreams,
            InitialConnectionWindowSize = o.Http2.InitialConnectionWindowSize,
            InitialStreamWindowSize = o.Http2.InitialStreamWindowSize,
            MaxFrameSize = o.Http2.MaxFrameSize,
            HeaderTableSize = o.Http2.HeaderTableSize,
            MaxHeaderListSize = o.Http2.MaxHeaderListSize ?? o.Limits.MaxRequestHeadersTotalSize,
            MaxHeaderCount = o.Limits.MaxRequestHeaderCount,
            MaxResponseBufferSize = o.Http2.MaxResponseBufferSize,
            BodyBufferThreshold = o.BodyBufferThreshold,
            ResponseBodyChunkSize = o.ResponseBodyChunkSize,
            BodyConsumptionTimeout = o.BodyConsumptionTimeout,
        };

    public static Http3ConnectionOptions ToHttp3Options(this TurboServerOptions o)
        => new()
        {
            Limits = ResolveLimits(o, o.Http3.MaxRequestBodySize, o.Http3.KeepAliveTimeout,
                o.Http3.RequestHeadersTimeout, o.Http3.MinRequestBodyDataRate,
                o.Http3.MinRequestBodyDataRateGracePeriod, o.Http3.MinResponseDataRate,
                o.Http3.MinResponseDataRateGracePeriod),
            MaxConcurrentStreams = o.Http3.MaxConcurrentStreams,
            MaxHeaderListSize = o.Http3.MaxHeaderListSize ?? o.Limits.MaxRequestHeadersTotalSize,
            MaxHeaderCount = o.Limits.MaxRequestHeaderCount,
            QpackMaxTableCapacity = o.Http3.QpackMaxTableCapacity,
            QpackBlockedStreams = o.Http3.QpackBlockedStreams,
            BodyBufferThreshold = o.BodyBufferThreshold,
            ResponseBodyChunkSize = o.ResponseBodyChunkSize,
            BodyConsumptionTimeout = o.BodyConsumptionTimeout,
        };

    public static DataRateOptions ToRateMonitor(this Http1ConnectionOptions o) => RateOf(o.Limits);
    public static DataRateOptions ToRateMonitor(this Http2ConnectionOptions o) => RateOf(o.Limits);
    public static DataRateOptions ToRateMonitor(this Http3ConnectionOptions o) => RateOf(o.Limits);

    private static DataRateOptions RateOf(in ResolvedServerLimits l)
        => new(l.MinRequestBodyDataRate, l.MinRequestBodyDataRateGracePeriod,
               l.MinResponseDataRate, l.MinResponseDataRateGracePeriod);

    private static ResolvedServerLimits ResolveLimits(
        TurboServerOptions o,
        long? maxBody, TimeSpan? keepAlive, TimeSpan? headersTimeout,
        double? minReqRate, TimeSpan? minReqGrace, double? minRespRate, TimeSpan? minRespGrace)
        => new(
            MaxRequestBodySize: maxBody ?? o.Limits.MaxRequestBodySize,
            KeepAliveTimeout: keepAlive ?? o.Limits.KeepAliveTimeout,
            RequestHeadersTimeout: headersTimeout ?? o.Limits.RequestHeadersTimeout,
            MinRequestBodyDataRate: minReqRate ?? o.Limits.MinRequestBodyDataRate,
            MinRequestBodyDataRateGracePeriod: minReqGrace ?? o.Limits.MinRequestBodyDataRateGracePeriod,
            MinResponseDataRate: minRespRate ?? o.Limits.MinResponseDataRate,
            MinResponseDataRateGracePeriod: minRespGrace ?? o.Limits.MinResponseDataRateGracePeriod);
}
