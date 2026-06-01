using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server.Options;

public sealed class ProtocolOptionsNullableOverrideSpec
{
    [Fact(Timeout = 5000)]
    public void Shared_overrides_should_default_to_null()
    {
        var h1 = new Http1ServerOptions();
        var h2 = new Http2ServerOptions();
        var h3 = new Http3ServerOptions();

        Assert.Null(h1.MaxRequestBodySize);
        Assert.Null(h1.MinRequestBodyDataRate);
        Assert.Null(h1.MinResponseDataRate);
        Assert.Null(h2.KeepAliveTimeout);
        Assert.Null(h2.MinRequestBodyDataRate);
        Assert.Null(h2.MinResponseDataRate);
        Assert.Null(h3.MaxRequestBodySize);
        Assert.Null(h3.KeepAliveTimeout);
        Assert.Null(h3.MinResponseDataRate);
    }

    [Fact(Timeout = 5000)]
    public void Setting_overrides_should_compile_via_implicit_conversion()
    {
        var h2 = new Http2ServerOptions
        {
            KeepAliveTimeout = TimeSpan.FromSeconds(60),
            MinRequestBodyDataRate = 240,
        };

        Assert.Equal(TimeSpan.FromSeconds(60), h2.KeepAliveTimeout);
        Assert.Equal(240d, h2.MinRequestBodyDataRate);
    }
}
