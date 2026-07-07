using GaudiHTTP.Tests.TestSupport;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http2.Client;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client.StateMachine;

public sealed class Http2StateMachineKeepAliveSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void OnTimerFired_should_emit_ping_frame_on_keepalive_timer()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(keepAlivePingDelay: TimeSpan.FromSeconds(10), keepAlivePingTimeout: TimeSpan.FromSeconds(20)), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        sm.OnTimerFired("keep-alive-ping");

        Assert.Single(ops.Outbound.OfType<TransportData>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void OnTimerFired_should_not_emit_duplicate_ping_when_awaiting_ack()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(keepAlivePingDelay: TimeSpan.FromSeconds(10), keepAlivePingTimeout: TimeSpan.FromSeconds(20)), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        sm.OnTimerFired("keep-alive-ping");
        sm.OnTimerFired("keep-alive-ping");

        Assert.Single(ops.Outbound.OfType<TransportData>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void OnTimerFired_should_not_close_when_timeout_not_elapsed()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(keepAlivePingDelay: TimeSpan.FromSeconds(10), keepAlivePingTimeout: TimeSpan.FromSeconds(20)), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        sm.OnTimerFired("keep-alive-ping");
        sm.OnTimerFired("keep-alive-ping-timeout");

        Assert.True(sm.CanAcceptRequest);
    }
}