using System.Text;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerConnectionPersistenceSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void ServerStateMachine_should_default_to_persistent_connection_for_http11()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        sm.ConnectTransport(
            Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n"),
            ops
        );

        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void ServerStateMachine_should_close_connection_after_http10_request()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        sm.ConnectTransport(
            Encoding.ASCII.GetBytes("GET / HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n"),
            ops
        );

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void ServerStateMachine_should_close_connection_when_connection_close_header()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        sm.ConnectTransport(
            Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.com\r\nConnection: close\r\nContent-Length: 0\r\n\r\n"),
            ops
        );

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void ServerStateMachine_should_track_pending_requests_via_can_accept_response()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        sm.ConnectTransport(
            Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n"),
            ops
        );

        Assert.True(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void ServerStateMachine_should_inject_connection_close_when_flagged()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);
        var transport = sm.ConnectTransport(
            Encoding.ASCII.GetBytes("GET / HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n"),
            ops
        );

        var context = ServerTestContext.CreateResponse();
        sm.OnResponse(context);

        Assert.True(transport.WrittenCount > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void ServerStateMachine_should_clear_pending_requests_on_cleanup()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        sm.ConnectTransport(
            Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n"),
            ops
        );
        Assert.True(sm.CanAcceptResponse);

        sm.Cleanup();

        Assert.False(sm.CanAcceptResponse);
    }
}



