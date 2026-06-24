using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

public sealed class Http3KeepAliveCloseSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.1")]
    public void OnTimerFired_should_set_ShouldComplete_on_keepalive_timeout()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerStateMachine(new GaudiServerOptions().ToHttp3Options(), ops);
        sm.PreStart();

        sm.OnTimerFired("keep-alive-timeout");

        Assert.True(sm.ShouldComplete);
    }
}
