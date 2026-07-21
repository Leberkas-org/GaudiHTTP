using System.Text;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerPipeliningSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_decode_two_pipelined_requests_from_single_buffer()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);
        var request = string.Concat(
            "GET / HTTP/1.1\r\n",
            "Host: example.com\r\n",
            "Content-Length: 0\r\n",
            "\r\n",
            "GET /page2 HTTP/1.1\r\n",
            "Host: example.com\r\n",
            "Content-Length: 0\r\n",
            "\r\n");
        var data = Encoding.ASCII.GetBytes(request);

        _ = sm.ConnectTransport(data, ops);

        Assert.Equal(2, ops.Requests.Count);
        Assert.Equal("/", ops.Requests[0].Get<IHttpRequestFeature>()?.Path);
        Assert.Equal("/page2", ops.Requests[1].Get<IHttpRequestFeature>()?.Path);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_process_responses_fifo_for_pipelined_requests()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);
        var request = string.Concat(
            "GET / HTTP/1.1\r\n",
            "Host: example.com\r\n",
            "Content-Length: 0\r\n",
            "\r\n",
            "GET /page2 HTTP/1.1\r\n",
            "Host: example.com\r\n",
            "Content-Length: 0\r\n",
            "\r\n");
        var data = Encoding.ASCII.GetBytes(request);

        var transport = sm.ConnectTransport(data, ops);

        var context1 = ServerTestContext.CreateResponse();
        sm.OnResponse(context1);

        var context2 = ServerTestContext.CreateResponse();
        sm.OnResponse(context2);

        Assert.True(transport.WrittenCount > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_throw_when_responding_without_pending_request()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        var context = ServerTestContext.CreateResponse();

        Assert.Throws<InvalidOperationException>(() => sm.OnResponse(context));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_handle_three_pipelined_requests()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);
        var request = string.Concat(
            "GET /page1 HTTP/1.1\r\n",
            "Host: example.com\r\n",
            "Content-Length: 0\r\n",
            "\r\n",
            "GET /page2 HTTP/1.1\r\n",
            "Host: example.com\r\n",
            "Content-Length: 0\r\n",
            "\r\n",
            "GET /page3 HTTP/1.1\r\n",
            "Host: example.com\r\n",
            "Content-Length: 0\r\n",
            "\r\n");
        var data = Encoding.ASCII.GetBytes(request);

        _ = sm.ConnectTransport(data, ops);

        Assert.Equal(3, ops.Requests.Count);
        Assert.Equal("/page1", ops.Requests[0].Get<IHttpRequestFeature>()?.Path);
        Assert.Equal("/page2", ops.Requests[1].Get<IHttpRequestFeature>()?.Path);
        Assert.Equal("/page3", ops.Requests[2].Get<IHttpRequestFeature>()?.Path);
    }
}

