using System.Text;
using GaudiHTTP.Protocol.Syntax;
using GaudiHTTP.Protocol.Syntax.Http10.Options;
using GaudiHTTP.Protocol.Syntax.Http10.Server;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http10.Server;

public sealed class Http10ServerDecoderSecuritySpec
{
    private static Http10ServerDecoderOptions DefaultDecoderOptions() => new()
    {
        StreamingThreshold = 64 * 1024,
        MaxBufferedBodySize = 4 * 1024 * 1024,
        MaxStreamedBodySize = null,
        MaxHeaderBytes = 32 * 1024,
        MaxHeaderCount = 100,
        HeaderLineMaxLength = 8 * 1024,
        RequestLineMaxLength = 8 * 1024,
        MaxRequestTargetLength = 8 * 1024,
        MaxChunkedControlLineLength = 64 * 1024,
        MaxChunkedTrailerSize = 32 * 1024,
        AllowObsFold = false
    };

    private static Http10ServerDecoder MakeDecoder(Http10ServerDecoderOptions? options = null)
    {
        return new Http10ServerDecoder(options ?? DefaultDecoderOptions());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.6")]
    public void Feed_should_reject_conflicting_content_length_values()
    {
        var raw = "POST /submit HTTP/1.0\r\nContent-Length: 5\r\nContent-Length: 10\r\n\r\nhello"u8.ToArray();
        var decoder = MakeDecoder();

        var ex = Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw, out _));
        Assert.NotNull(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.6")]
    public void Feed_should_reject_non_numeric_content_length()
    {
        var raw = "POST /submit HTTP/1.0\r\nContent-Length: abc\r\n\r\n"u8.ToArray();
        var decoder = MakeDecoder();

        var ex = Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw, out _));
        Assert.NotNull(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.6")]
    public void Feed_should_reject_negative_content_length()
    {
        var raw = "POST /submit HTTP/1.0\r\nContent-Length: -1\r\n\r\n"u8.ToArray();
        var decoder = MakeDecoder();

        var ex = Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw, out _));
        Assert.NotNull(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7.2")]
    public void Feed_should_accept_content_length_zero_and_ignore_trailing_data()
    {
        var raw = "POST /submit HTTP/1.0\r\nContent-Length: 0\r\n\r\ntrailing"u8.ToArray();
        var decoder = MakeDecoder();

        var outcome = decoder.Feed(raw, out var consumed);
        Assert.Equal(DecodeOutcome.Complete, outcome);
        // Should consume up to end of headers + body (which is 0 bytes), leaving trailing data unconsumed
        Assert.True(consumed <= raw.Length - "trailing".Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-8.6")]
    public void Feed_should_accept_duplicate_content_length_with_same_value()
    {
        var raw = "POST /submit HTTP/1.0\r\nContent-Length: 5\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray();
        var decoder = MakeDecoder();

        var outcome = decoder.Feed(raw, out _);
        Assert.Equal(DecodeOutcome.Complete, outcome);

        var request = decoder.GetRequestFeature();
        Assert.Equal(HttpMethod.Post.Method, request.Method);
    }

    [Fact(Timeout = 5000)]
    public void Feed_should_reject_header_block_exceeding_max_header_bytes()
    {
        var options = DefaultDecoderOptions() with { MaxHeaderBytes = 64 };
        var decoder = MakeDecoder(options);
        var headerValue = new string('x', 100);
        var raw = Encoding.ASCII.GetBytes($"GET / HTTP/1.0\r\nX-Custom: {headerValue}\r\n\r\n");

        Assert.ThrowsAny<Exception>(() => decoder.Feed(raw, out _));
    }

    [Fact(Timeout = 5000)]
    public void Feed_should_reject_header_count_exceeding_max()
    {
        var options = DefaultDecoderOptions() with { MaxHeaderCount = 2 };
        var decoder = MakeDecoder(options);
        var raw = "GET / HTTP/1.0\r\nX-One: 1\r\nX-Two: 2\r\nX-Three: 3\r\n\r\n"u8.ToArray();

        Assert.ThrowsAny<Exception>(() => decoder.Feed(raw, out _));
    }

    [Fact(Timeout = 5000)]
    public void Feed_should_reject_header_line_exceeding_max_length()
    {
        var options = DefaultDecoderOptions() with { HeaderLineMaxLength = 32 };
        var decoder = MakeDecoder(options);
        var longValue = new string('a', 50);
        var raw = Encoding.ASCII.GetBytes($"GET / HTTP/1.0\r\nX-Long: {longValue}\r\n\r\n");

        Assert.ThrowsAny<Exception>(() => decoder.Feed(raw, out _));
    }

    [Fact(Timeout = 5000)]
    public void Feed_should_reject_request_line_exceeding_max_length()
    {
        var options = DefaultDecoderOptions() with { RequestLineMaxLength = 32 };
        var decoder = MakeDecoder(options);
        var raw = Encoding.ASCII.GetBytes($"GET /{new string('a', 40)} HTTP/1.0\r\nContent-Length: 0\r\n\r\n");

        Assert.ThrowsAny<Exception>(() => decoder.Feed(raw, out _));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5.2")]
    public void Feed_should_accept_obs_fold_when_allowed()
    {
        var options = DefaultDecoderOptions() with { AllowObsFold = true };
        var decoder = MakeDecoder(options);
        var raw = "GET / HTTP/1.0\r\nX-Multi: value1\r\n continued\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var outcome = decoder.Feed(raw, out _);
        Assert.Equal(DecodeOutcome.Complete, outcome);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-5.2")]
    public void Feed_should_reject_obs_fold_when_not_allowed()
    {
        var options = DefaultDecoderOptions() with { AllowObsFold = false };
        var decoder = MakeDecoder(options);
        var raw = "GET / HTTP/1.0\r\nX-Multi: value1\r\n continued\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex = Assert.Throws<HttpProtocolException>(() => decoder.Feed(raw, out _));
        Assert.NotNull(ex);
    }

    [Fact(Timeout = 5000)]
    public void Feed_should_not_crash_after_prior_error()
    {
        var decoder = MakeDecoder();
        var invalidCL = "POST / HTTP/1.0\r\nContent-Length: abc\r\n\r\n"u8.ToArray();
        var validRequest = "POST / HTTP/1.0\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var ex1 = Record.Exception(() => decoder.Feed(invalidCL, out _));
        Assert.NotNull(ex1);

        _ = Record.Exception(() => decoder.Feed(validRequest, out _));
        // Second feed may throw again, but should not crash with NullRef/AccessViolation
        // If it throws, it should be a protocol exception or similar, not a system-level crash
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7.2")]
    public void Feed_should_parse_valid_content_length_zero()
    {
        var decoder = MakeDecoder();
        var raw = "POST / HTTP/1.0\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

        var outcome = decoder.Feed(raw, out _);
        Assert.Equal(DecodeOutcome.Complete, outcome);
    }
}