using TurboHTTP.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server;

public sealed class Http2ServerOptionsResolutionSpec
{
    [Fact(Timeout = 5000)]
    public void Null_keepalive_override_should_resolve_to_limits()
    {
        var o = new TurboServerOptions
        {
            Limits =
            {
                KeepAliveTimeout = TimeSpan.FromSeconds(42)
            }
        };

        var eff = o.ToHttp2Options();

        Assert.Equal(TimeSpan.FromSeconds(42), eff.Limits.KeepAliveTimeout);
    }
}
