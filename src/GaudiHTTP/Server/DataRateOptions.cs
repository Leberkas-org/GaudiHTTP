namespace GaudiHTTP.Server;

internal readonly record struct DataRateOptions(
    double MinRequestBodyDataRate,
    TimeSpan MinRequestBodyDataRateGracePeriod,
    double MinResponseDataRate,
    TimeSpan MinResponseDataRateGracePeriod);
