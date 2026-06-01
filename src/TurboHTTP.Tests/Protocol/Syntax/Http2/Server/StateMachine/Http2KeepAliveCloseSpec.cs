using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.StateMachine;

public sealed class Http2KeepAliveCloseSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.5")]
    public void OnTimerFired_should_set_ShouldComplete_on_keepalive_timeout()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions().ToHttp2Options(), ops);
        sm.PreStart();

        sm.OnTimerFired("keep-alive-timeout");

        Assert.True(sm.ShouldComplete);
    }
}
