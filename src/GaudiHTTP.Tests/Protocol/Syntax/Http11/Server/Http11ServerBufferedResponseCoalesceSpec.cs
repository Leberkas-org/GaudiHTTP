using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerBufferedResponseCoalesceSpec
{
    private static Http11ServerStateMachine CreateSm(FakeServerOps ops)
        => new(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

    private static InMemoryTransport SendRequest(Http11ServerStateMachine sm, FakeServerOps ops)
    {
        var data = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n");
        return sm.ConnectTransport(data, ops);
    }

    private static IFeatureCollection BufferedResponse(byte[] body, bool withContentLength)
    {
        var features = ServerTestContext.CreateResponse();
        if (withContentLength)
        {
            features.Get<IHttpResponseFeature>()!.Headers["Content-Length"] = new StringValues(body.Length.ToString());
        }

        // A fully-buffered, completed response body (the dominant Plaintext/Json case): written to
        // the buffer writer and completed without ever upgrading to a pipe, so TryGetBufferedBody
        // hands the bytes back synchronously.
        var bodyFeature = (GaudiHttpResponseBodyFeature)features.Get<IHttpResponseBodyFeature>()!;
        var span = bodyFeature.Writer.GetSpan(body.Length);
        body.CopyTo(span);
        bodyFeature.Writer.Advance(body.Length);
        bodyFeature.Complete();

        return features;
    }

    [Fact(Timeout = 5000)]
    public void Buffered_content_length_response_is_emitted_as_a_single_outbound()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var transport = SendRequest(sm, ops);

        var body = "hello world"u8.ToArray();
        sm.OnResponse(BufferedResponse(body, withContentLength: true));

        var text = Encoding.ASCII.GetString(transport.WrittenSpan);

        Assert.Contains("HTTP/1.1 200", text);
        Assert.Contains("Content-Length: 11", text);
        Assert.EndsWith("hello world", text);
    }

    [Fact(Timeout = 5000)]
    public void Buffered_coalesced_response_still_signals_body_complete()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        _ = SendRequest(sm, ops);

        var features = BufferedResponse("xyz"u8.ToArray(), withContentLength: true);
        sm.OnResponse(features);

        Assert.Contains(features, ops.ResponseBodyCompletions);
    }

    [Fact(Timeout = 5000)]
    public void Chunked_buffered_response_is_not_coalesced()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var transport = SendRequest(sm, ops);

        sm.OnResponse(BufferedResponse("hello world"u8.ToArray(), withContentLength: false));

        var text = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.Contains("Transfer-Encoding: chunked", text);
        Assert.Contains("0\r\n\r\n", text);
    }

    [Fact(Timeout = 5000)]
    public void Buffered_coalesced_response_rearms_keep_alive_timer()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        _ = SendRequest(sm, ops);

        // Coalesced completion runs through the CompleteResponse epilogue synchronously —
        // no pump/body message round-trip is involved on this path.
        sm.OnResponse(BufferedResponse("xyz"u8.ToArray(), withContentLength: true));

        Assert.Contains(ops.ScheduledTimers, t => t.Name == "keep-alive");
    }
}
