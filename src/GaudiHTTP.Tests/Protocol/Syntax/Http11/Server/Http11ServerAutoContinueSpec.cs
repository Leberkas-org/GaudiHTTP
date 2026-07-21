using System.Text;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerAutoContinueSpec
{
    private static Http11ServerStateMachine CreateSm(FakeServerOps ops)
    {
        var options = new GaudiServerOptions();
        return new Http11ServerStateMachine(options.ToHttp1Options(), options.ToHttp2Options(), ops);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public void Server_should_auto_send_100_continue_when_expect_header_present()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var data = Encoding.ASCII.GetBytes(
            "POST /upload HTTP/1.1\r\nHost: example.com\r\nExpect: 100-continue\r\nContent-Length: 5\r\n\r\nhello");
        var transport = sm.ConnectTransport(data, ops);

        Assert.Single(ops.Requests);
        var wire = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.Contains("HTTP/1.1 100", wire);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public void Server_should_not_auto_send_100_without_expect_header()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var data = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\nHost: example.com\r\n\r\n");
        var transport = sm.ConnectTransport(data, ops);

        Assert.Single(ops.Requests);
        var wire = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.DoesNotContain("100", wire);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public void Final_response_should_follow_auto_100_continue()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var data = Encoding.ASCII.GetBytes(
            "POST /upload HTTP/1.1\r\nHost: example.com\r\nExpect: 100-continue\r\nContent-Length: 5\r\n\r\nhello");
        var transport = sm.ConnectTransport(data, ops);

        var features = ops.Requests[0];
        var responseFeature = features.Get<IHttpResponseFeature>()!;
        responseFeature.StatusCode = 200;
        sm.OnResponse(features);

        var wire = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.Contains("HTTP/1.1 100", wire);
        Assert.Contains("HTTP/1.1 200", wire);
    }
}
