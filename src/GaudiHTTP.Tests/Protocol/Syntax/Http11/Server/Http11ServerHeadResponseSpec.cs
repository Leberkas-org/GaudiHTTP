using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

/// <summary>
/// RFC 9110 §9.3.2 / RFC 9112 §6.3: a response to a HEAD request carries the same header fields a
/// GET would (incl. Content-Length) but MUST NOT include a message body. Emitting body octets after
/// a HEAD desynchronizes a keep-alive connection (the next response is mis-framed).
/// </summary>
public sealed class Http11ServerHeadResponseSpec
{
    private static Http11ServerStateMachine CreateSm(FakeServerOps ops)
    {
        var options = new TurboServerOptions();
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

    private static IFeatureCollection BuildResponse(string method, string body)
    {
        var fc = new TurboFeatureCollection();
        fc.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature { Method = method });

        var responseFeature = new GaudiHttpResponseFeature
        {
            StatusCode = 200,
            Headers =
            {
                ["Content-Length"] = body.Length.ToString()
            }
        };

        var bodyFeature = new GaudiHttpResponseBodyFeature();
        bodyFeature.SetResponseFeature(responseFeature);
        var bytes = Encoding.ASCII.GetBytes(body);
        var mem = bodyFeature.Writer.GetMemory(bytes.Length);
        bytes.CopyTo(mem.Span);
        bodyFeature.Writer.Advance(bytes.Length);
        bodyFeature.Writer.Complete();

        fc.Set<IHttpResponseFeature>(responseFeature);
        fc.Set<IHttpResponseBodyFeature>(bodyFeature);
        return fc;
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
    [Trait("RFC", "RFC9110-9.3.2")]
    public void OnResponse_should_suppress_body_for_HEAD_request()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.DecodeClientData(Make("HEAD / HTTP/1.1\r\nHost: example.com\r\n\r\n"));

        sm.OnResponse(BuildResponse("HEAD", "hello"));

        var wire = Outbound(ops);
        Assert.Contains("HTTP/1.1 200", wire);
        Assert.DoesNotContain("hello", wire);
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_should_emit_body_for_GET_request()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.DecodeClientData(Make("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n"));

        sm.OnResponse(BuildResponse("GET", "hello"));

        var wire = Outbound(ops);
        Assert.Contains("HTTP/1.1 200", wire);
        Assert.Contains("hello", wire);
    }
}
