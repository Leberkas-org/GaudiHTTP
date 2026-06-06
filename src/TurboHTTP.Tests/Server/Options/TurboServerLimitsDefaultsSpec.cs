using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server.Options;

public sealed class TurboServerLimitsDefaultsSpec
{
    [Fact(Timeout = 5000)]
    public void Defaults_should_match_Kestrel_parity()
    {
        var limits = new TurboServerLimits();

        Assert.Equal(30_000_000L, limits.MaxRequestBodySize);
        Assert.Equal(TimeSpan.FromSeconds(130), limits.KeepAliveTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), limits.RequestHeadersTimeout);
        Assert.Equal(240d, limits.MinRequestBodyDataRate);
        Assert.Equal(TimeSpan.FromSeconds(5), limits.MinRequestBodyDataRateGracePeriod);
        Assert.Equal(240d, limits.MinResponseDataRate);
        Assert.Equal(TimeSpan.FromSeconds(5), limits.MinResponseDataRateGracePeriod);
    }
}
