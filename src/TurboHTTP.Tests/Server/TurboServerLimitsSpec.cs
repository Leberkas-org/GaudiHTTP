using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class TurboServerLimitsSpec
{
    [Fact(Timeout = 5000)]
    public void MaxConcurrentRequests_should_default_to_zero_meaning_unlimited()
    {
        var limits = new TurboServerLimits();
        Assert.Equal(0, limits.MaxConcurrentRequests);
    }

    [Fact(Timeout = 5000)]
    public void MaxConcurrentRequests_should_be_settable()
    {
        var limits = new TurboServerLimits { MaxConcurrentRequests = 512 };
        Assert.Equal(512, limits.MaxConcurrentRequests);
    }

    [Fact(Timeout = 5000)]
    public void MaxConcurrentConnections_should_default_to_zero_meaning_unlimited()
    {
        var limits = new TurboServerLimits();
        Assert.Equal(0, limits.MaxConcurrentConnections);
    }

    [Fact(Timeout = 5000)]
    public void MinRequestGuarantee_should_default_to_ten()
    {
        var limits = new TurboServerLimits();
        Assert.Equal(10, limits.MinRequestGuarantee);
    }
}
