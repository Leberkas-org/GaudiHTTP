using System.Text;
using System.Text.RegularExpressions;
using Akka.Actor;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Protocol.Syntax.Http11.Options;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Client;

public sealed class Http11ClientEncoderSpec
{
    private readonly Http11ClientEncoder _encoder = new(Http11ClientEncoderOptions.Default);

    [Fact(Timeout = 5000)]
    public void Encode_should_write_request_line()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, request, ActorRefs.Nobody);

        Assert.True(written > 0);
        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("GET /path HTTP/1.1", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_add_host_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/path");
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, request, ActorRefs.Nobody);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("Host: example.com:8080", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_write_headers_with_content_length()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent("test body"u8.ToArray())
        };
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, request, ActorRefs.Nobody);

        Assert.True(written > 0);
        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("POST / HTTP/1.1", result);
        Assert.Contains("Content-Length: 9", result);
    }

    [Fact(Timeout = 5000)]
    public void Encode_should_write_connection_header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, request, ActorRefs.Nobody);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.Contains("Connection:", result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-2.2")]
    public void Encode_should_end_headers_with_crlf_crlf()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, request, ActorRefs.Nobody);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.True(result.Contains("\r\n"), "Output should use CRLF line endings");
        Assert.True(result.EndsWith("\r\n\r\n"), "Headers must end with CRLF CRLF");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5.1")]
    public void Encode_should_separate_header_block_from_body_with_blank_line()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, request, ActorRefs.Nobody);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        Assert.True(result.Contains("\r\n\r\n"),
            "Header block must be separated from body with blank line (CRLF CRLF)");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-2.1")]
    public void Encode_should_format_request_line_correctly()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api/resource");
        var buffer = new byte[4096];

        var written = _encoder.Encode(buffer, request, ActorRefs.Nobody);

        var result = Encoding.ASCII.GetString(buffer, 0, written);
        var firstLine = result[..result.IndexOf("\r\n")];
        Assert.True(Regex.IsMatch(firstLine, @"^POST /api/resource HTTP/1\.1$"),
            $"Request line should be formatted correctly, got: '{firstLine}'");
    }
}