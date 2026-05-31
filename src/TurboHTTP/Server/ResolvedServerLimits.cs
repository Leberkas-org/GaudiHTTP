namespace TurboHTTP.Server;

internal readonly record struct ResolvedServerLimits(
    long MaxRequestBodySize,
    TimeSpan KeepAliveTimeout,
    TimeSpan RequestHeadersTimeout,
    double MinRequestBodyDataRate,
    TimeSpan MinRequestBodyDataRateGracePeriod,
    double MinResponseDataRate,
    TimeSpan MinResponseDataRateGracePeriod);
