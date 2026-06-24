using GaudiHTTP.Server;

namespace GaudiHTTP.Tests.Server;

public sealed class TurboServerLimitsSpec
{
    [Fact(Timeout = 5000)]
    public void MaxConcurrentConnections_should_default_to_zero_meaning_unlimited()
    {
        var limits = new TurboServerLimits();
        Assert.Equal(0, limits.MaxConcurrentConnections);
    }
}
