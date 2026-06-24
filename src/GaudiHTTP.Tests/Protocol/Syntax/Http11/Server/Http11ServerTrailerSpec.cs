using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerTrailerSpec
{
    private static Http11ServerStateMachine CreateSm(FakeServerOps ops)
        => new(new TurboServerOptions().ToHttp1Options(), new TurboServerOptions().ToHttp2Options(), ops);

    private static void SendRequest(Http11ServerStateMachine sm)
    {
        var data = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n");
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        sm.DecodeClientData(TransportData.Rent(buffer));
    }

    private static IFeatureCollection ChunkedResponseWithTrailers(
        byte[] body,
        GaudiHttpResponseTrailersFeature trailerFeature)
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature { Method = "GET" });

        var responseFeature = new GaudiHttpResponseFeature { StatusCode = 200 };
        // No Content-Length → state machine will use chunked transfer encoding
        features.Set<IHttpResponseFeature>(responseFeature);
        features.Set<IHttpResponseTrailersFeature>(trailerFeature);

        // Fully buffered, completed response body (no Content-Length → EmitBufferedBody path with chunked)
        var bodyFeature = new GaudiHttpResponseBodyFeature();
        var span = bodyFeature.Writer.GetSpan(body.Length);
        body.CopyTo(span);
        bodyFeature.Writer.Advance(body.Length);
        bodyFeature.Complete();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);

        return features;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.2")]
    public void EmitBufferedBody_should_emit_trailer_section_when_trailers_present()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        var trailerFeature = new GaudiHttpResponseTrailersFeature();
        trailerFeature.Trailers["x-checksum"] = new StringValues("abc123");

        sm.OnResponse(ChunkedResponseWithTrailers("hello"u8.ToArray(), trailerFeature));

        var wireBytes = ops.Outbound
            .OfType<TransportData>()
            .SelectMany(td => td.Buffer.Span.ToArray())
            .ToArray();
        var wireText = Encoding.ASCII.GetString(wireBytes);

        Assert.DoesNotContain("0\r\n\r\n", wireText);
        Assert.Contains("0\r\n", wireText);
        Assert.Contains("x-checksum: abc123\r\n", wireText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7.1.2")]
    public void EmitBufferedBody_should_emit_plain_terminator_when_no_trailers()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        var trailerFeature = new GaudiHttpResponseTrailersFeature();
        // No trailers added

        sm.OnResponse(ChunkedResponseWithTrailers("hello"u8.ToArray(), trailerFeature));

        var wireBytes = ops.Outbound
            .OfType<TransportData>()
            .SelectMany(td => td.Buffer.Span.ToArray())
            .ToArray();
        var wireText = Encoding.ASCII.GetString(wireBytes);

        Assert.Contains("0\r\n\r\n", wireText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5.1")]
    public void EmitBufferedBody_should_filter_prohibited_trailers()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        var trailerFeature = new GaudiHttpResponseTrailersFeature();
        trailerFeature.Trailers["x-checksum"] = new StringValues("abc123");
        trailerFeature.Trailers["transfer-encoding"] = new StringValues("chunked");
        trailerFeature.Trailers["content-length"] = new StringValues("5");

        sm.OnResponse(ChunkedResponseWithTrailers("hello"u8.ToArray(), trailerFeature));

        var wireBytes = ops.Outbound
            .OfType<TransportData>()
            .SelectMany(td => td.Buffer.Span.ToArray())
            .ToArray();
        var wireText = Encoding.ASCII.GetString(wireBytes);

        Assert.Contains("x-checksum: abc123\r\n", wireText);
        Assert.DoesNotContain("transfer-encoding: chunked", wireText);
        Assert.DoesNotContain("content-length: 5", wireText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public void EmitBufferedBody_should_freeze_trailers_before_encoding()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        var trailerFeature = new GaudiHttpResponseTrailersFeature();
        trailerFeature.Trailers["x-checksum"] = new StringValues("abc123");

        sm.OnResponse(ChunkedResponseWithTrailers("hello"u8.ToArray(), trailerFeature));

        Assert.True(trailerFeature.Trailers.IsReadOnly);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public void OnResponse_should_add_trailer_header_when_trailers_present()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        var trailerFeature = new GaudiHttpResponseTrailersFeature();
        trailerFeature.Trailers["x-checksum"] = new StringValues("abc123");
        trailerFeature.Trailers["x-timing"] = new StringValues("42ms");

        sm.OnResponse(ChunkedResponseWithTrailers("hello"u8.ToArray(), trailerFeature));

        var wireBytes = ops.Outbound
            .OfType<TransportData>()
            .SelectMany(td => td.Buffer.Span.ToArray())
            .ToArray();
        var wireText = Encoding.ASCII.GetString(wireBytes);

        // The Trailer response header should announce the trailer field names
        // Split headers and body to avoid matching trailer names in the trailer section
        var headerEndIndex = wireText.IndexOf("\r\n\r\n");
        var headerSection = wireText.Substring(0, headerEndIndex);
        Assert.Contains("Trailer: ", headerSection);
        Assert.Contains("x-checksum", headerSection);
        Assert.Contains("x-timing", headerSection);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.5")]
    public void OnResponse_should_not_add_trailer_header_when_no_trailers()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        var trailerFeature = new GaudiHttpResponseTrailersFeature();
        // No trailers

        sm.OnResponse(ChunkedResponseWithTrailers("hello"u8.ToArray(), trailerFeature));

        var headerData = ops.Outbound.OfType<TransportData>().First();
        var headerText = Encoding.ASCII.GetString(headerData.Buffer.Span);

        Assert.DoesNotContain("Trailer:", headerText);
    }
}
