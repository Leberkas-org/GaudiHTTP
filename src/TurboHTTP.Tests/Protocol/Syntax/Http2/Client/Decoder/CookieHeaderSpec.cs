using System.Net;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Client;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Client.Decoder;

public sealed class CookieHeaderSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.3")]
    public void HpackDecoder_should_preserve_multiple_cookie_headers_as_separate_entries()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var block = encoder.Encode([
            ("cookie", "a=1"),
            ("cookie", "b=2"),
            ("cookie", "c=3")
        ]);

        var decoder = new HpackDecoder();
        var headers = decoder.Decode(block.Span);

        var cookieHeaders = headers.Where(h => h.Name == "cookie").ToList();
        Assert.Equal(3, cookieHeaders.Count);
        Assert.Equal("a=1", cookieHeaders[0].Value);
        Assert.Equal("b=2", cookieHeaders[1].Value);
        Assert.Equal("c=3", cookieHeaders[2].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.3")]
    public void ResponseDecoder_should_receive_multiple_cookie_headers_from_hpack()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var block = encoder.Encode([
            (":status", "200"),
            ("cookie", "a=1"),
            ("cookie", "b=2")
        ]);

        var state = new StreamState();
        state.AppendHeader(block.Span);
        var decoder = new Http2ClientDecoder(16 * 1024, 64 * 1024);
        var response = decoder.DecodeHeaders(1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("cookie"));

        var cookieValues = response.Headers.GetValues("cookie").ToList();
        Assert.NotEmpty(cookieValues);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.3")]
    public void ResponseDecoder_should_accept_single_cookie_header_unchanged()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var block = encoder.Encode([
            (":status", "200"),
            ("cookie", "session=abc123")
        ]);

        var state = new StreamState();
        state.AppendHeader(block.Span);
        var decoder = new Http2ClientDecoder(16 * 1024, 64 * 1024);
        var response = decoder.DecodeHeaders(1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("cookie"));
        var cookieValues = response.Headers.GetValues("cookie").ToList();
        Assert.Single(cookieValues);
        Assert.Equal("session=abc123", cookieValues[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.3")]
    public void HpackEncoder_should_encode_cookie_headers_independently()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var block = encoder.Encode([
            (":status", "200"),
            ("cookie", "a=1"),
            ("cookie", "b=2")
        ]);

        var decoder = new HpackDecoder();
        var headers = decoder.Decode(block.Span);

        var cookieHeaders = headers.Where(h => h.Name == "cookie").ToList();
        Assert.Equal(2, cookieHeaders.Count);
        Assert.Equal("a=1", cookieHeaders[0].Value);
        Assert.Equal("b=2", cookieHeaders[1].Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.3")]
    public void ResponseDecoder_should_handle_empty_cookie_value()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var block = encoder.Encode([
            (":status", "200"),
            ("cookie", "")
        ]);

        var state = new StreamState();
        state.AppendHeader(block.Span);
        var decoder = new Http2ClientDecoder(16 * 1024, 64 * 1024);
        var response = decoder.DecodeHeaders(1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.3")]
    public void ResponseDecoder_should_preserve_all_cookie_headers()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var cookies = new List<(string, string)>
        {
            (":status", "200"),
            ("cookie", "session1=value1"),
            ("cookie", "session2=value2"),
            ("cookie", "session3=value3"),
            ("cookie", "session4=value4")
        };
        var block = encoder.Encode(cookies);

        var state = new StreamState();
        state.AppendHeader(block.Span);
        var decoder = new Http2ClientDecoder(16 * 1024, 64 * 1024);
        var response = decoder.DecodeHeaders(1, endStream: true, state);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("cookie"));

        var cookieValues = response.Headers.GetValues("cookie").ToList();
        Assert.Equal(4, cookieValues.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2.3")]
    public void CookieHeadersCanBeSplitForCompressionEfficiency()
    {
        var encoder = new HpackEncoder(useHuffman: false);

        var unsplit = encoder.Encode([
            ("cookie", "a=1; b=2; c=3")
        ]);

        var split = encoder.Encode([
            ("cookie", "a=1"),
            ("cookie", "b=2"),
            ("cookie", "c=3")
        ]);

        var decoderForUnsplit = new HpackDecoder();
        var decoderForSplit = new HpackDecoder();

        var unsplitHeaders = decoderForUnsplit.Decode(unsplit.Span);
        var splitHeaders = decoderForSplit.Decode(split.Span);

        var unsplitCookieValue = unsplitHeaders.Where(h => h.Name == "cookie").Select(h => h.Value).FirstOrDefault();
        var splitCookieValues = splitHeaders.Where(h => h.Name == "cookie").Select(h => h.Value).ToList();

        Assert.NotNull(unsplitCookieValue);
        Assert.Equal(1, splitCookieValues.Count(v => v.Contains("a=1")));
        Assert.Equal(1, splitCookieValues.Count(v => v.Contains("b=2")));
        Assert.Equal(1, splitCookieValues.Count(v => v.Contains("c=3")));
    }
}
