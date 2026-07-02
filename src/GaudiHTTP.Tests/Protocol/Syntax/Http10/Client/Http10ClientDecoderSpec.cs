using System.Net;
using GaudiHTTP.Protocol.Syntax;
using GaudiHTTP.Protocol.Syntax.Http10.Client;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http10.Client;

public sealed class Http10ClientDecoderSpec
{
    private static Http10ClientDecoder MakeDecoder() =>
        new(ClientOptionDefaults.Http10Decoder());

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Decoder_should_parse_status_line_and_headers()
    {
        var raw = "HTTP/1.0 200 OK\r\nContent-Length: 0\r\nX-Custom: foo\r\n\r\n"u8.ToArray();
        var decoder = MakeDecoder();

        var result = decoder.Feed(raw, requestMethodWasHead: false, out var consumed);
        Assert.Equal(DecodeOutcome.Complete, result);
        Assert.Equal(raw.Length, consumed);

        var response = decoder.GetResponse();
        Assert.Equal(HttpVersion.Version10, response.Version);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", response.ReasonPhrase);
        Assert.True(response.Headers.Contains("X-Custom"));
        Assert.Equal(0, response.Content.Headers.ContentLength);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void Decoder_should_signal_NeedMore_when_data_incomplete()
    {
        var partial = "HTTP/1.0 200 OK\r\nContent-Len"u8.ToArray();
        Assert.Equal(DecodeOutcome.NeedMore, MakeDecoder().Feed(partial, false, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6.2")]
    public async Task Decoder_should_attach_buffered_body_below_threshold()
    {
        var raw = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray();
        var decoder = MakeDecoder();
        Assert.Equal(DecodeOutcome.Complete, decoder.Feed(raw, false, out _));

        var response = decoder.GetResponse();
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", System.Text.Encoding.ASCII.GetString(bytes));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6.2")]
    public async Task Decoder_should_stream_body_above_threshold()
    {
        var opts = ClientOptionDefaults.Http10Decoder() with { StreamingThreshold = 4 };
        var raw = "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray();
        var decoder = new Http10ClientDecoder(opts);

        Assert.Equal(DecodeOutcome.Complete, decoder.Feed(raw, false, out _));
        var response = decoder.GetResponse();
        Assert.IsType<StreamContent>(response.Content);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello", System.Text.Encoding.ASCII.GetString(bytes));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-3.1")]
    public async Task Decoder_should_treat_non_HTTP_prefix_as_HTTP09_response()
    {
        var raw = "<html>old server</html>"u8.ToArray();
        var decoder = MakeDecoder();
        Assert.Equal(DecodeOutcome.HeadersReady, decoder.Feed(raw, false, out _));
        decoder.SignalEof();

        var response = decoder.GetResponse();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal("<html>old server</html>", System.Text.Encoding.ASCII.GetString(bytes));
    }

    [Fact(Timeout = 5000)]
    public void Decoder_should_handle_HEAD_response_with_no_body()
    {
        var raw = "HTTP/1.0 200 OK\r\nContent-Length: 1000\r\n\r\n"u8.ToArray();
        var decoder = MakeDecoder();
        Assert.Equal(DecodeOutcome.Complete, decoder.Feed(raw, requestMethodWasHead: true, out _));

        var response = decoder.GetResponse();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6.1")]
    public void Decoder_should_reject_embedded_cr_in_status_line()
    {
        var raw = "HTTP/1.0 200\rOK\r\n\r\n"u8.ToArray();
        var decoder = MakeDecoder();
        var result = decoder.Feed(raw, false, out _);

        Assert.NotEqual(DecodeOutcome.Complete, result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7.2")]
    public void Decoder_should_skip_1xx_and_parse_final_response()
    {
        var raw = "HTTP/1.0 100 Continue\r\n\r\nHTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var decoder = MakeDecoder();
        decoder.Feed(raw, false, out _);

        var response = decoder.GetResponse();
        Assert.True((int)response.StatusCode >= 100);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-3.1")]
    public void Decoder_should_tolerate_leading_zeros_in_version()
    {
        var raw = "HTTP/01.00 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var decoder = MakeDecoder();
        var result = decoder.Feed(raw, false, out _);

        Assert.Equal(DecodeOutcome.Complete, result);
        var response = decoder.GetResponse();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-3.3")]
    public void Decoder_should_accept_response_with_rfc850_date()
    {
        var raw = "HTTP/1.0 200 OK\r\nDate: Sunday, 06-Nov-94 08:49:37 GMT\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var decoder = MakeDecoder();
        var result = decoder.Feed(raw, false, out _);

        Assert.Equal(DecodeOutcome.Complete, result);
        var response = decoder.GetResponse();
        Assert.True(response.Headers.Contains("Date"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-3.6")]
    public void Decoder_should_parse_content_type_with_extra_whitespace()
    {
        var raw = "HTTP/1.0 200 OK\r\nContent-Type: text/html\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var decoder = MakeDecoder();
        Assert.Equal(DecodeOutcome.Complete, decoder.Feed(raw, false, out _));

        var response = decoder.GetResponse();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-3.6")]
    public void Decoder_should_parse_content_type_ignoring_unknown_parameters()
    {
        var raw = "HTTP/1.0 200 OK\r\nContent-Type: text/html; charset=utf-8; foo=bar\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var decoder = MakeDecoder();
        Assert.Equal(DecodeOutcome.Complete, decoder.Feed(raw, false, out _));

        var response = decoder.GetResponse();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}