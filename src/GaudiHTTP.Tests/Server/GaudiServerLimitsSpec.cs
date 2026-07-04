using GaudiHTTP.Server;

namespace GaudiHTTP.Tests.Server;

public sealed class GaudiServerLimitsSpec
{
    [Fact(Timeout = 5000)]
    public void MaxConcurrentConnections_should_default_to_zero_meaning_unlimited()
    {
        var limits = new GaudiServerLimits();
        Assert.Equal(0, limits.MaxConcurrentConnections);
    }

    [Fact(Timeout = 5000)]
    public void StartupTimeout_default_should_be_25_seconds()
    {
        var o = new GaudiServerOptions();

        Assert.Equal(TimeSpan.FromSeconds(25), o.StartupTimeout);
    }

    [Fact(Timeout = 5000)]
    public void StartupTimeout_should_be_settable()
    {
        var o = new GaudiServerOptions { StartupTimeout = TimeSpan.FromSeconds(60) };

        Assert.Equal(TimeSpan.FromSeconds(60), o.StartupTimeout);
    }

    [Fact(Timeout = 5000)]
    public void MaxProtocolSniffBytes_default_should_be_64KB()
    {
        var limits = new GaudiServerLimits();

        Assert.Equal(64 * 1024, limits.MaxProtocolSniffBytes);
    }

    [Fact(Timeout = 5000)]
    public void MaxProtocolSniffBytes_should_be_settable()
    {
        var limits = new GaudiServerLimits { MaxProtocolSniffBytes = 128 * 1024 };

        Assert.Equal(128 * 1024, limits.MaxProtocolSniffBytes);
    }
}
