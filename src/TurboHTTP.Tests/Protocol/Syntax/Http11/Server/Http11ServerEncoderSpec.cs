using System.Text;
using Akka.Actor;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Protocol.Syntax.Http11.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerEncoderSpec
{
    private readonly Http11ServerEncoder _encoder = new(Http11ServerEncoderOptions.Default);

    [Fact(Timeout = 5000)]
    public void Encode_should_write_status_line()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
            ReasonPhrase = "OK"
        };
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: false);

        Assert.True(written > 0);
        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("HTTP/1.1 200 OK", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_add_content_length()
    {
        var body = "test body"u8.ToArray();
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body),
            ReasonPhrase = "OK"
        };
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: false);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("Content-Length: 9", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_handle_chunked_response()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("chunked"u8.ToArray()),
            ReasonPhrase = "OK"
        };
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: true);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("HTTP/1.1 200 OK", result);
        Assert.DoesNotContain("Content-Length", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_include_date_header()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
            ReasonPhrase = "OK"
        };
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: false);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("Date:", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-2.2")]
    public void Encode_should_not_produce_bare_cr_in_headers()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
            ReasonPhrase = "OK"
        };
        response.Headers.TryAddWithoutValidation("X-Test", "value\rwith\rcr");
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: false);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        for (var i = 0; i < result.Length; i++)
        {
            if (result[i] == '\r' && (i + 1 >= result.Length || result[i + 1] != '\n'))
            {
                Assert.Fail("Found bare CR at position " + i);
            }
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5.2")]
    public void Encode_should_not_produce_obs_fold_in_headers()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]),
            ReasonPhrase = "OK"
        };
        response.Headers.TryAddWithoutValidation("X-Long", new string('a', 200));
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: false);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.DoesNotContain("\r\n ", result.Replace("\r\n\r\n", "<<END>>"));
        Assert.DoesNotContain("\r\n\t", result.Replace("\r\n\r\n", "<<END>>"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void Encode_should_not_double_apply_chunked_transfer_encoding()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("test"u8.ToArray()),
            ReasonPhrase = "OK"
        };
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: true);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        var teCount = result.Split("chunked").Length - 1;
        Assert.True(teCount <= 1, $"chunked appeared {teCount} times");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void Encode_should_include_content_length_for_known_size_body()
    {
        var body = "known size body"u8.ToArray();
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body),
            ReasonPhrase = "OK"
        };
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, response, ActorRefs.Nobody, isChunked: false);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains($"Content-Length: {body.Length}", result);
    }
}