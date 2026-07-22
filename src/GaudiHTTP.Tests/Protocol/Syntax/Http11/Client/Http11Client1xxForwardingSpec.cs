using System.Net;
using System.Text;
using GaudiHTTP.Protocol.Syntax.Http11.Client;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Client;

public sealed class Http11Client1xxForwardingSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void Client_should_forward_100_continue_to_ops()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 1), ops);
        sm.OnRequest(new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload"));

        var responseBytes = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\nHTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
        sm.ConnectTransport(initialData: responseBytes, ops: ops);

        Assert.True(ops.Responses.Count >= 2);
        Assert.Equal(HttpStatusCode.Continue, ops.Responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, ops.Responses[1].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void Client_should_forward_103_early_hints_to_ops()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 1), ops);
        sm.OnRequest(new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));

        var responseBytes = Encoding.ASCII.GetBytes(
            "HTTP/1.1 103 Early Hints\r\nLink: </style.css>; rel=preload\r\n\r\nHTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
        sm.ConnectTransport(initialData: responseBytes, ops: ops);

        Assert.True(ops.Responses.Count >= 2);
        Assert.Equal((HttpStatusCode)103, ops.Responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, ops.Responses[1].StatusCode);
    }
}
