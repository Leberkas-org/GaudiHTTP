using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server.Options;

public sealed class ServerOptionsProjectionsSpec
{
    [Fact(Timeout = 5000)]
    public void Override_should_win_over_limits()
    {
        var o = new TurboServerOptions();
        o.Http2.MaxRequestBodySize = 999;
        o.Http2.KeepAliveTimeout = TimeSpan.FromSeconds(7);

        var eff = o.ToHttp2Options();

        Assert.Equal(999, eff.Limits.MaxRequestBodySize);
        Assert.Equal(TimeSpan.FromSeconds(7), eff.Limits.KeepAliveTimeout);
    }

    [Fact(Timeout = 5000)]
    public void Null_override_should_inherit_limits()
    {
        var o = new TurboServerOptions();

        var eff = o.ToHttp2Options();

        Assert.Equal(o.Limits.MaxRequestBodySize, eff.Limits.MaxRequestBodySize);
        Assert.Equal(o.Limits.KeepAliveTimeout, eff.Limits.KeepAliveTimeout);
        Assert.Equal(o.Limits.MinResponseDataRate, eff.Limits.MinResponseDataRate);
    }

    [Fact(Timeout = 5000)]
    public void Http3_body_override_should_now_be_honored()
    {
        var o = new TurboServerOptions();
        o.Http3.MaxRequestBodySize = 555;

        Assert.Equal(555, o.ToHttp3Options().Limits.MaxRequestBodySize);
    }

    [Fact(Timeout = 5000)]
    public void ToRateMonitor_should_project_four_rate_fields()
    {
        var eff = new TurboServerOptions().ToHttp2Options();

        var rate = eff.ToRateMonitor();

        Assert.Equal(eff.Limits.MinRequestBodyDataRate, rate.MinRequestBodyDataRate);
        Assert.Equal(eff.Limits.MinResponseDataRate, rate.MinResponseDataRate);
    }
}
