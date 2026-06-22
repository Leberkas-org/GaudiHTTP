using System.Net;
using System.Text;
using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Client;

public sealed class Http11Client1xxForwardingSpec
{
    private static TurboClientOptions MakeConfig()
        => new() { Http1 = new Http1ClientOptions { MaxPipelineDepth = 1 } };

    private static TransportData Make(string raw)
    {
        var data = Encoding.ASCII.GetBytes(raw);
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return TransportData.Rent(buffer);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void Client_should_forward_100_continue_to_ops()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(ops, MakeConfig());
        sm.OnRequest(new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload"));

        sm.DecodeServerData(Make("HTTP/1.1 100 Continue\r\n\r\nHTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"));

        Assert.True(ops.Responses.Count >= 2);
        Assert.Equal(HttpStatusCode.Continue, ops.Responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, ops.Responses[1].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void Client_should_forward_103_early_hints_to_ops()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(ops, MakeConfig());
        sm.OnRequest(new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));

        sm.DecodeServerData(Make(
            "HTTP/1.1 103 Early Hints\r\nLink: </style.css>; rel=preload\r\n\r\nHTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"));

        Assert.True(ops.Responses.Count >= 2);
        Assert.Equal((HttpStatusCode)103, ops.Responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, ops.Responses[1].StatusCode);
    }
}
