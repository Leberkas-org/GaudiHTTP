using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
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

    private static TransportData Make(string raw)
    {
        var data = Encoding.ASCII.GetBytes(raw);
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return TransportData.Rent(buffer);
    }

    private static string Outbound(FakeServerOps ops)
    {
        var sb = new StringBuilder();
        foreach (var item in ops.Outbound)
        {
            if (item is TransportData td)
            {
                sb.Append(Encoding.ASCII.GetString(td.Buffer.Span));
            }
        }

        return sb.ToString();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public void Server_should_auto_send_100_continue_when_expect_header_present()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.DecodeClientData(Make(
            "POST /upload HTTP/1.1\r\nHost: example.com\r\nExpect: 100-continue\r\nContent-Length: 5\r\n\r\nhello"));

        Assert.Single(ops.Requests);
        var wire = Outbound(ops);
        Assert.Contains("HTTP/1.1 100", wire);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public void Server_should_not_auto_send_100_without_expect_header()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.DecodeClientData(Make(
            "GET / HTTP/1.1\r\nHost: example.com\r\n\r\n"));

        Assert.Single(ops.Requests);
        var wire = Outbound(ops);
        Assert.DoesNotContain("100", wire);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-10.1.1")]
    public void Final_response_should_follow_auto_100_continue()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.DecodeClientData(Make(
            "POST /upload HTTP/1.1\r\nHost: example.com\r\nExpect: 100-continue\r\nContent-Length: 5\r\n\r\nhello"));

        var features = ops.Requests[0];
        var responseFeature = features.Get<IHttpResponseFeature>()!;
        responseFeature.StatusCode = 200;
        sm.OnResponse(features);

        var wire = Outbound(ops);
        Assert.Contains("HTTP/1.1 100", wire);
        Assert.Contains("HTTP/1.1 200", wire);
    }
}
