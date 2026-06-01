using System.Net;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Semantics.Body;

public sealed class BodySemanticsClassifierSpec
{
    private static HeaderCollection Headers(params (string n, string v)[] pairs)
    {
        var h = new HeaderCollection();
        foreach (var (n, v) in pairs)
        {
            h.Add(n, v);
        }

        return h;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.4")]
    public void Classify_should_return_None_for_response_to_HEAD()
    {
        var r = BodySemantics.ClassifyResponse(200, Headers(("Content-Length", "100")),
            HttpVersion.Version11, requestMethodWasHead: true, connectionWillClose: false);
        Assert.Equal(BodyFraming.None, r.Framing);
    }

    [Theory(Timeout = 5000)]
    [InlineData(100), InlineData(199), InlineData(204), InlineData(304)]
    [Trait("RFC", "RFC9110-6.4")]
    public void Classify_should_return_None_for_status_without_body(int code)
    {
        var r = BodySemantics.ClassifyResponse(code, Headers(("Content-Length", "100")),
            HttpVersion.Version11, false, false);
        Assert.Equal(BodyFraming.None, r.Framing);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void Classify_should_return_Chunked_when_TE_chunked_on_HTTP11()
    {
        var r = BodySemantics.ClassifyResponse(200, Headers(("Transfer-Encoding", "chunked")),
            HttpVersion.Version11, false, false);
        Assert.Equal(BodyFraming.Chunked, r.Framing);
        Assert.Null(r.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void Classify_should_reject_when_TE_chunked_on_HTTP10()
    {
        Assert.Throws<HttpProtocolException>(() =>
            BodySemantics.ClassifyResponse(200, Headers(("Transfer-Encoding", "chunked")),
                HttpVersion.Version10, false, true));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void Classify_should_return_Length_when_ContentLength_present()
    {
        var r = BodySemantics.ClassifyResponse(200, Headers(("Content-Length", "512")),
            HttpVersion.Version11, false, false);
        Assert.Equal(BodyFraming.Length, r.Framing);
        Assert.Equal(512, r.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void Classify_should_return_Close_when_no_framing_on_response()
    {
        var r = BodySemantics.ClassifyResponse(200, new HeaderCollection(),
            HttpVersion.Version11, false, true);
        Assert.Equal(BodyFraming.Close, r.Framing);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-6.4")]
    public void Classify_should_return_None_for_request_without_framing()
    {
        var r = BodySemantics.ClassifyRequest(HttpMethod.Get,
            new HeaderCollection(), HttpVersion.Version11);
        Assert.Equal(BodyFraming.None, r.Framing);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void Classify_should_reject_request_when_final_transfer_coding_is_not_chunked()
    {
        // RFC 9112 §6.1: a request whose final transfer coding is not chunked has no reliable body
        // length and MUST be rejected (400). Otherwise the body is parsed as the next request (smuggling).
        Assert.Throws<HttpProtocolException>(() =>
            BodySemantics.ClassifyRequest(HttpMethod.Post,
                Headers(("Transfer-Encoding", "gzip")), HttpVersion.Version11));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void Classify_should_reject_request_when_chunked_is_not_the_final_transfer_coding()
    {
        Assert.Throws<HttpProtocolException>(() =>
            BodySemantics.ClassifyRequest(HttpMethod.Post,
                Headers(("Transfer-Encoding", "chunked, gzip")), HttpVersion.Version11));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void Classify_should_accept_request_when_chunked_is_the_final_transfer_coding()
    {
        var r = BodySemantics.ClassifyRequest(HttpMethod.Post,
            Headers(("Transfer-Encoding", "gzip, chunked")), HttpVersion.Version11);
        Assert.Equal(BodyFraming.Chunked, r.Framing);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void Classify_should_not_treat_substring_chunked_token_as_chunked()
    {
        Assert.Throws<HttpProtocolException>(() =>
            BodySemantics.ClassifyRequest(HttpMethod.Post,
                Headers(("Transfer-Encoding", "x-chunked-ext")), HttpVersion.Version11));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void Classify_should_read_until_close_for_response_with_non_final_chunked()
    {
        // RFC 9112 §6.1: for a RESPONSE, a non-final chunked coding means read-until-close, not chunked.
        var r = BodySemantics.ClassifyResponse(200, Headers(("Transfer-Encoding", "chunked, gzip")),
            HttpVersion.Version11, false, connectionWillClose: true);
        Assert.Equal(BodyFraming.Close, r.Framing);
    }
}