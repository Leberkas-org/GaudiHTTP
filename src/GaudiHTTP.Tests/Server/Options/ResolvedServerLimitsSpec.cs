using GaudiHTTP.Server;

namespace GaudiHTTP.Tests.Server.Options;

public sealed class ResolvedServerLimitsSpec
{
    [Fact(Timeout = 5000)]
    public void Should_hold_all_six_resolved_values()
    {
        var r = new ResolvedServerLimits(
            MaxRequestBodySize: 123,
            KeepAliveTimeout: TimeSpan.FromSeconds(10),
            RequestHeadersTimeout: TimeSpan.FromSeconds(20),
            MinRequestBodyDataRate: 1,
            MinRequestBodyDataRateGracePeriod: TimeSpan.FromSeconds(3),
            MinResponseDataRate: 2,
            MinResponseDataRateGracePeriod: TimeSpan.FromSeconds(4),
            MaxResetStreamsPerWindow: 200,
            RapidResetDetectionWindow: TimeSpan.FromSeconds(30));

        Assert.Equal(123, r.MaxRequestBodySize);
        Assert.Equal(2, r.MinResponseDataRate);
    }
}
