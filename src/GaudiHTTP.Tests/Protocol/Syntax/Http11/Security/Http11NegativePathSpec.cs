using System.Text;
using GaudiHTTP.Protocol.Syntax;
using GaudiHTTP.Protocol.Syntax.Http11.Client;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Security;

public sealed class Http11NegativePathSpec
{
    private static List<HttpResponseMessage> Decode(ReadOnlyMemory<byte> data, bool isHead = false)
    {
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());
        var responses = new List<HttpResponseMessage>();
        var offset = 0;
        while (offset < data.Length)
        {
            var outcome = decoder.Feed(data[offset..], isHead, out var consumed);
            if (outcome == DecodeOutcome.NeedMore)
            {
                break;
            }

            offset += consumed;
            if (outcome == DecodeOutcome.Complete)
            {
                responses.Add(decoder.GetResponse());
                decoder.Reset();
            }
        }

        return responses;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11NegativePath_should_parse_http20_version()
    {
        var raw = "HTTP/2.0 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());

        var outcome = decoder.Feed(raw.AsMemory(), false, out _);

        Assert.Equal(DecodeOutcome.Complete, outcome);
        var resp = decoder.GetResponse();
        Assert.Equal(new Version(2, 0), resp.Version);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11NegativePath_should_treat_non_http_protocol_as_http09()
    {
        // "HTTPS/1.1" does not start with "HTTP/", so the decoder treats it as HTTP/0.9 body data.
        var raw = "HTTPS/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());

        var outcome = decoder.Feed(raw.AsMemory(), false, out _);

        Assert.NotEqual(DecodeOutcome.Complete, outcome);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11NegativePath_should_need_more_when_double_space_before_status_code()
    {
        // RFC 9112 §4: exactly one SP between HTTP-version and 3-digit status code.
        // The parser returns false (NeedMore) for a malformed status line.
        var raw = "HTTP/1.1  200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());

        var outcome = decoder.Feed(raw.AsMemory(), false, out _);

        Assert.Equal(DecodeOutcome.NeedMore, outcome);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11NegativePath_should_need_more_when_two_digit_status_code()
    {
        // RFC 9112 §4: status-code is exactly 3 decimal digits.
        // The parser returns false (NeedMore) for a malformed status line.
        var raw = "HTTP/1.1 20 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());

        var outcome = decoder.Feed(raw.AsMemory(), false, out _);

        Assert.Equal(DecodeOutcome.NeedMore, outcome);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11NegativePath_should_need_more_when_non_digit_in_status_code()
    {
        // The parser returns false (NeedMore) for a malformed status-code.
        var raw = "HTTP/1.1 20A OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());

        var outcome = decoder.Feed(raw.AsMemory(), false, out _);

        Assert.Equal(DecodeOutcome.NeedMore, outcome);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11NegativePath_should_never_decode_when_bare_line_feed_in_status_line()
    {
        // RFC 9112 §2.2: a recipient MUST NOT treat a bare LF as a line terminator.
        // Bare-LF input is treated as incomplete data (NeedMore).
        var raw = "HTTP/1.1 200 OK\nContent-Length: 0\n\n"u8.ToArray();
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());

        var outcome = decoder.Feed(raw.AsMemory(), false, out _);

        Assert.Equal(DecodeOutcome.NeedMore, outcome);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Http11NegativePath_should_decode_when_overlong_reason_phrase()
    {
        // The status-line parser does not enforce a length limit on the reason phrase;
        // it reads to CRLF. Only header-block bytes count toward MaxHeaderBytes.
        var longReason = new string('X', 66000);
        var raw = Encoding.ASCII.GetBytes($"HTTP/1.1 200 {longReason}\r\nContent-Length: 0\r\n\r\n");
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());

        var outcome = decoder.Feed(raw.AsMemory(), false, out _);

        Assert.Equal(DecodeOutcome.Complete, outcome);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public async Task Http11NegativePath_should_complete_when_chunked_trailer_without_colon()
    {
        // The chunked body decoder does not validate trailer field syntax — it only
        // scans for the terminal empty CRLF. Invalid trailer lines are silently skipped.
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "5\r\nHello\r\n" +
            "0\r\n" +
            "InvalidTrailerNoColon\r\n" +
            "\r\n";
        var raw = Encoding.ASCII.GetBytes(response);
        var responses = Decode(raw);

        Assert.Single(responses);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5")]
    public async Task Http11NegativePath_should_complete_when_empty_field_name_in_trailer()
    {
        // The chunked body decoder does not validate trailer field names.
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "5\r\nHello\r\n" +
            "0\r\n" +
            ": EmptyName\r\n" +
            "\r\n";
        var raw = Encoding.ASCII.GetBytes(response);
        var responses = Decode(raw);

        Assert.Single(responses);
        Assert.Equal("Hello", await responses[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Http11NegativePath_should_need_more_when_non_chunked_te_without_content_length()
    {
        // Transfer-Encoding: gzip without Content-Length → close-delimited framing.
        // Per RFC 9112 §6.3: message body length determined by octets before connection close.
        // The decoder returns NeedMore until EOF is signaled.
        var raw = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: gzip\r\n" +
            "\r\n");
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());

        var outcome = decoder.Feed(raw.AsMemory(), false, out _);

        Assert.Equal(DecodeOutcome.NeedMore, outcome);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public async Task Http11NegativePath_should_treat_as_pipelined_response_when_bytes_after_content_length()
    {
        // RFC 9112 §6.3: extra bytes after declared body are treated as next pipelined response.
        const string twoResponses =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 5\r\n" +
            "\r\n" +
            "Hello" +
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 3\r\n" +
            "\r\n" +
            "Bye";
        var raw = Encoding.ASCII.GetBytes(twoResponses);

        var responses = Decode(raw);

        Assert.Equal(2, responses.Count);
        Assert.Equal("Hello"u8.ToArray(),
            await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Bye"u8.ToArray(),
            await responses[1].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15")]
    public async Task Http11NegativePath_should_have_empty_body_when_response_204()
    {
        // RFC 9110 §15.3.5: A 204 response MUST NOT include a message body.
        var raw = "HTTP/1.1 204 No Content\r\nContent-Length: 10\r\n\r\n"u8.ToArray();
        var responses = Decode(raw);

        Assert.Single(responses);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, responses[0].StatusCode);
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15")]
    public async Task Http11NegativePath_should_have_empty_body_when_response_304()
    {
        // RFC 9110 §15.4.5: A 304 response MUST NOT contain a message body.
        var raw = "HTTP/1.1 304 Not Modified\r\nContent-Length: 20\r\nETag: \"abc\"\r\n\r\n"u8.ToArray();
        var responses = Decode(raw);

        Assert.Single(responses);
        Assert.Equal(System.Net.HttpStatusCode.NotModified, responses[0].StatusCode);
        Assert.Empty(await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public async Task Http11NegativePath_should_accept_when_multiple_content_length_same_value()
    {
        // RFC 9112 §6.3: multiple Content-Length with identical values is treated as a single header.
        // BodySemantics de-duplicates comma-joined values when all parts match.
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 5\r\n" +
            "Content-Length: 5\r\n" +
            "\r\n" +
            "Hello";
        var raw = Encoding.ASCII.GetBytes(response);
        var responses = Decode(raw);

        Assert.Single(responses);
        Assert.Equal("Hello"u8.ToArray(),
            await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11NegativePath_should_reject_when_multiple_content_length_different_values()
    {
        // RFC 9112 §6.3: differing Content-Length values MUST be rejected (smuggling prevention).
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Length: 5\r\n" +
            "Content-Length: 10\r\n" +
            "\r\n" +
            "Hello";
        var raw = Encoding.ASCII.GetBytes(response);
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());

        Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw.AsMemory(), false, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9")]
    public void Http11NegativePath_should_reject_when_transfer_encoding_and_content_length()
    {
        // RFC 9112 §6.3: TE + CL desync — smuggling prevention.
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "Content-Length: 5\r\n" +
            "\r\n" +
            "5\r\nHello\r\n0\r\n\r\n";
        var raw = Encoding.ASCII.GetBytes(response);
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());

        Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw.AsMemory(), false, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public void Http11NegativePath_should_reject_when_chunked_zero_size_non_numeric_characters()
    {
        // RFC 9112 §7.1: chunk-size = 1*HEXDIG; "0x5" uses "0x" prefix which is not valid HEXDIG.
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "0x5\r\nHello\r\n" +
            "0\r\n\r\n";
        var raw = Encoding.ASCII.GetBytes(response);
        var decoder = new Http11ClientDecoder(ClientOptionDefaults.Http11Decoder());

        Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw.AsMemory(), false, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-7")]
    public async Task Http11NegativePath_should_accept_when_chunked_upper_case_hex_size()
    {
        // RFC 9112 §7.1: HEXDIG includes both upper and lower case A-F.
        const string response =
            "HTTP/1.1 200 OK\r\n" +
            "Transfer-Encoding: chunked\r\n" +
            "\r\n" +
            "A\r\n0123456789\r\n" +
            "0\r\n\r\n";
        var raw = Encoding.ASCII.GetBytes(response);
        var responses = Decode(raw);

        Assert.Single(responses);
        var body = await responses[0].Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(10, body.Length);
        Assert.Equal("0123456789"u8.ToArray(), body);
    }
}