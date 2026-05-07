using System.Text;
using TurboHTTP.Protocol.Http11;

namespace TurboHTTP.Tests.Http11.Decoding;

public sealed class Http11StatusLineDecoderPeekSpec
{
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    [InlineData("HTTP/1.1 200 OK\r\n", 200)]
    [InlineData("HTTP/1.1 404 Not Found\r\n", 404)]
    [InlineData("HTTP/1.1 500 Internal Server Error\r\n", 500)]
    [InlineData("HTTP/1.1 301 Moved Permanently\r\n", 301)]
    public void PeekCode_should_extract_status_code(string line, int expected)
    {
        var bytes = Encoding.ASCII.GetBytes(line);
        Assert.Equal(expected, StatusLineDecoder.PeekCode(bytes));
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    [InlineData("")]
    [InlineData("HTTP/1.1")]
    [InlineData("short")]
    public void PeekCode_should_return_null_when_buffer_too_short(string line)
    {
        var bytes = Encoding.ASCII.GetBytes(line);
        Assert.Null(StatusLineDecoder.PeekCode(bytes));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void PeekCode_should_return_null_when_no_space_found()
    {
        var bytes = Encoding.ASCII.GetBytes("XXXXXXXXXXXXXXXX");
        Assert.Null(StatusLineDecoder.PeekCode(bytes));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void PeekCode_should_return_null_when_first_digit_out_of_range()
    {
        var bytes = Encoding.ASCII.GetBytes("HTTP/1.1 099 Invalid\r\n");
        Assert.Null(StatusLineDecoder.PeekCode(bytes));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void PeekCode_should_return_null_when_first_digit_above_5()
    {
        var bytes = Encoding.ASCII.GetBytes("HTTP/1.1 600 Invalid\r\n");
        Assert.Null(StatusLineDecoder.PeekCode(bytes));
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    [InlineData("HTTP/1.1 200 OK\r\n", 200, "OK")]
    [InlineData("HTTP/1.1 201 Created\r\n", 201, "Created")]
    [InlineData("HTTP/1.1 202 Accepted\r\n", 202, "Accepted")]
    [InlineData("HTTP/1.1 204 No Content\r\n", 204, "No Content")]
    [InlineData("HTTP/1.1 301 Moved Permanently\r\n", 301, "Moved Permanently")]
    [InlineData("HTTP/1.1 302 Found\r\n", 302, "Found")]
    [InlineData("HTTP/1.1 304 Not Modified\r\n", 304, "Not Modified")]
    [InlineData("HTTP/1.1 400 Bad Request\r\n", 400, "Bad Request")]
    [InlineData("HTTP/1.1 401 Unauthorized\r\n", 401, "Unauthorized")]
    [InlineData("HTTP/1.1 403 Forbidden\r\n", 403, "Forbidden")]
    [InlineData("HTTP/1.1 404 Not Found\r\n", 404, "Not Found")]
    [InlineData("HTTP/1.1 500 Internal Server Error\r\n", 500, "Internal Server Error")]
    public void TryParse_should_parse_well_known_reason_phrases(string line, int expectedCode, string expectedReason)
    {
        var lineBytes = Encoding.ASCII.GetBytes(line.TrimEnd('\r', '\n'));
        var result = StatusLineDecoder.TryParse(lineBytes, out var code, out var reason);

        Assert.True(result);
        Assert.Equal(expectedCode, code);
        Assert.Equal(expectedReason, reason);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    [InlineData("HTTP/1.1 206 Partial Content\r\n", 206, "Partial Content")]
    [InlineData("HTTP/1.1 299 Custom Reason\r\n", 299, "Custom Reason")]
    [InlineData("HTTP/1.1 418 I'm A Teapot\r\n", 418, "I'm A Teapot")]
    public void TryParse_should_parse_non_standard_reason_phrases(string line, int expectedCode, string expectedReason)
    {
        var lineBytes = Encoding.ASCII.GetBytes(line.TrimEnd('\r', '\n'));
        var result = StatusLineDecoder.TryParse(lineBytes, out var code, out var reason);

        Assert.True(result);
        Assert.Equal(expectedCode, code);
        Assert.Equal(expectedReason, reason);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    [InlineData("")]
    [InlineData("HTTP/1.")]
    [InlineData("HTTP/1.1 20")]
    [InlineData("GARBAGE")]
    public void TryParse_should_fail_on_invalid_status_lines(string line)
    {
        var lineBytes = Encoding.ASCII.GetBytes(line);
        Assert.False(StatusLineDecoder.TryParse(lineBytes, out _, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void TryParse_should_fail_when_version_prefix_wrong()
    {
        var lineBytes = "HTTZ/1.1 200 OK"u8;
        Assert.False(StatusLineDecoder.TryParse(lineBytes, out _, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void TryParse_should_fail_when_status_code_out_of_range()
    {
        var lineBytes = "HTTP/1.1 999 Overflow"u8;
        Assert.False(StatusLineDecoder.TryParse(lineBytes, out _, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void TryParse_should_parse_status_line_without_reason_phrase()
    {
        var lineBytes = "HTTP/1.1 200"u8;
        var result = StatusLineDecoder.TryParse(lineBytes, out var code, out var reason);

        Assert.True(result);
        Assert.Equal(200, code);
        Assert.Equal(string.Empty, reason);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void TryParse_should_fail_when_non_numeric_status_code()
    {
        var lineBytes = "HTTP/1.1 abc OK"u8;
        Assert.False(StatusLineDecoder.TryParse(lineBytes, out _, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void TryParse_should_handle_http_10_version()
    {
        var lineBytes = "HTTP/1.0 200 OK"u8;
        var result = StatusLineDecoder.TryParse(lineBytes, out var code, out _);

        Assert.True(result);
        Assert.Equal(200, code);
    }
}
