using TurboHTTP.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server;

public sealed class Http3ServerOptionsResolutionSpec
{
    [Fact(Timeout = 5000)]
    public void Body_override_should_win_else_limits()
    {
        var o = new TurboServerOptions
        {
            Http3 =
            {
                MaxRequestBodySize = 777
            }
        };
        Assert.Equal(777, o.ToHttp3Options().Limits.MaxRequestBodySize);

        var o2 = new TurboServerOptions
        {
            Limits =
            {
                MaxRequestBodySize = 888
            }
        };
        Assert.Equal(888, o2.ToHttp3Options().Limits.MaxRequestBodySize);
    }

    [Fact(Timeout = 5000)]
    public void QpackBlockedStreams_should_flow_from_Http3ServerOptions_to_ConnectionOptions()
    {
        var opts = new TurboServerOptions
        {
            Http3 =
            {
                QpackBlockedStreams = 42
            }
        };
        Assert.Equal(42, opts.ToHttp3Options().QpackBlockedStreams);
    }

    [Fact(Timeout = 5000)]
    public void QpackBlockedStreams_default_should_be_100()
    {
        var opts = new TurboServerOptions();
        Assert.Equal(100, opts.ToHttp3Options().QpackBlockedStreams);
    }
}
