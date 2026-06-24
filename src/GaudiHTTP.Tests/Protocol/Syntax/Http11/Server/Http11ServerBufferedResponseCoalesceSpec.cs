using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerBufferedResponseCoalesceSpec
{
    private static Http11ServerStateMachine CreateSm(FakeServerOps ops)
        => new(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

    private static void SendRequest(Http11ServerStateMachine sm)
    {
        var data = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n");
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        sm.DecodeClientData(TransportData.Rent(buffer));
    }

    private static IFeatureCollection BufferedResponse(byte[] body, bool withContentLength)
    {
        var features = new GaudiFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature { Method = "GET" });

        var responseFeature = new GaudiHttpResponseFeature { StatusCode = 200 };
        if (withContentLength)
        {
            responseFeature.Headers["Content-Length"] = new StringValues(body.Length.ToString());
        }

        features.Set<IHttpResponseFeature>(responseFeature);

        // A fully-buffered, completed response body (the dominant Plaintext/Json case): written to
        // the buffer writer and completed without ever upgrading to a pipe, so TryGetBufferedBody
        // hands the bytes back synchronously.
        var bodyFeature = new GaudiHttpResponseBodyFeature();
        var span = bodyFeature.Writer.GetSpan(body.Length);
        body.CopyTo(span);
        bodyFeature.Writer.Advance(body.Length);
        bodyFeature.Complete();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);

        return features;
    }

    [Fact(Timeout = 5000)]
    public void Buffered_content_length_response_is_emitted_as_a_single_outbound()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        var body = "hello world"u8.ToArray();
        sm.OnResponse(BufferedResponse(body, withContentLength: true));

        var item = Assert.Single(ops.Outbound);
        var data = Assert.IsType<TransportData>(item);
        var text = Encoding.ASCII.GetString(data.Buffer.Span);

        Assert.Contains("HTTP/1.1 200", text);
        Assert.Contains("Content-Length: 11", text);
        Assert.EndsWith("hello world", text);
    }

    [Fact(Timeout = 5000)]
    public void Buffered_coalesced_response_still_signals_body_complete()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        var features = BufferedResponse("xyz"u8.ToArray(), withContentLength: true);
        sm.OnResponse(features);

        Assert.Contains(features, ops.ResponseBodyCompletions);
    }

    [Fact(Timeout = 5000)]
    public void Chunked_buffered_response_is_not_coalesced()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        // No Content-Length -> chunked framing: status/header buffer, framed chunk, and the
        // zero-length terminator stay as separate outbound items.
        sm.OnResponse(BufferedResponse("hello world"u8.ToArray(), withContentLength: false));

        Assert.True(ops.Outbound.Count > 1);
    }
}
