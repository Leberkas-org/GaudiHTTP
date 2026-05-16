using TurboHTTP.Protocol;

namespace TurboHTTP.Tests.Protocol;

public sealed class WellKnownHeadersSpec
{
    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_return_interned_string_for_known_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("Host"u8);
        Assert.Equal("Host", result.Name);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_allocate_string_for_unknown_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("X-Custom-Header"u8);
        Assert.Equal("X-Custom-Header", result.Name);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_intern_2_char_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("TE"u8);
        Assert.Equal("TE", result.Name);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_intern_3_char_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("Age"u8);
        Assert.Equal("Age", result.Name);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_intern_4_char_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("Date"u8);
        Assert.Equal("Date", result.Name);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_intern_10_char_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("Connection"u8);
        Assert.Equal("Connection", result.Name);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_intern_13_char_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("Authorization"u8);
        Assert.Equal("Authorization", result.Name);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_intern_25_char_names()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName("Strict-Transport-Security"u8);
        Assert.Equal("Strict-Transport-Security", result.Name);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderValue_should_return_interned_value_for_known_values()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderValue("gzip"u8);
        Assert.Equal("gzip", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderValue_should_allocate_string_for_unknown_values()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderValue("x-custom-encoding"u8);
        Assert.Equal("x-custom-encoding", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderValue_should_intern_1_char_values()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderValue("0"u8);
        Assert.Equal("0", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderValue_should_intern_2_char_values()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderValue("br"u8);
        Assert.Equal("br", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderValue_should_intern_10_char_values()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderValue("keep-alive"u8);
        Assert.Equal("keep-alive", result);
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_return_true_for_identical_case_insensitive_ascii()
    {
        var a = "Content-Type"u8;
        var b = "content-type"u8;
        Assert.True(WellKnownHeaders.EqualsIgnoreCase(a, b));
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_return_false_for_different_lengths()
    {
        var a = "Host"u8;
        var b = "Content-Type"u8;
        Assert.False(WellKnownHeaders.EqualsIgnoreCase(a, b));
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_return_false_for_different_content()
    {
        var a = "Host"u8;
        var b = "Date"u8;
        Assert.False(WellKnownHeaders.EqualsIgnoreCase(a, b));
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_return_true_for_exact_match()
    {
        var a = "Host"u8;
        var b = "Host"u8;
        Assert.True(WellKnownHeaders.EqualsIgnoreCase(a, b));
    }


    [Theory(Timeout = 5000)]
    [InlineData("0", "0")]
    [InlineData("1", "1")]
    [InlineData("br", "br")]
    [InlineData("gzip", "gzip")]
    [InlineData("none", "none")]
    [InlineData("close", "close")]
    [InlineData("bytes", "bytes")]
    [InlineData("public", "public")]
    [InlineData("chunked", "chunked")]
    [InlineData("deflate", "deflate")]
    [InlineData("private", "private")]
    [InlineData("trailer", "trailer")]
    [InlineData("compress", "compress")]
    [InlineData("identity", "identity")]
    [InlineData("no-cache", "no-cache")]
    [InlineData("no-store", "no-store")]
    [InlineData("trailers", "trailers")]
    [InlineData("keep-alive", "keep-alive")]
    [InlineData("max-age=300", "max-age=300")]
    [InlineData("max-age=604800", "max-age=604800")]
    [InlineData("application/json", "application/json")]
    [InlineData("application/octet-stream", "application/octet-stream")]
    public void GetOrCreateHeaderValue_should_intern_well_known_values(string input, string expected)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(input);
        var result = WellKnownHeaders.GetOrCreateHeaderValue(bytes);
        Assert.Equal(expected, result);
    }

    [Theory(Timeout = 5000)]
    [InlineData("2")]
    [InlineData("zstd")]
    [InlineData("custom")]
    [InlineData("unknown-value")]
    [InlineData("text/html")]
    [InlineData("text/plain")]
    public void GetOrCreateHeaderValue_should_allocate_for_unknown_values(string input)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(input);
        var result = WellKnownHeaders.GetOrCreateHeaderValue(bytes);
        Assert.Equal(input, result);
    }
}

public sealed class WellKnownHeaderNameExtendedSpec
{
    [Theory(Timeout = 5000)]
    [InlineData("Via", "Via")]
    [InlineData("ETag", "ETag")]
    [InlineData("Vary", "Vary")]
    [InlineData("From", "From")]
    [InlineData("Link", "Link")]
    [InlineData("Allow", "Allow")]
    [InlineData("Accept", "Accept")]
    [InlineData("Cookie", "Cookie")]
    [InlineData("Expect", "Expect")]
    [InlineData("Pragma", "Pragma")]
    [InlineData("Server", "Server")]
    [InlineData("Alt-Svc", "Alt-Svc")]
    [InlineData("Expires", "Expires")]
    [InlineData("Referer", "Referer")]
    [InlineData("Trailer", "Trailer")]
    [InlineData("Upgrade", "Upgrade")]
    [InlineData("Warning", "Warning")]
    [InlineData("If-Match", "If-Match")]
    [InlineData("If-Range", "If-Range")]
    [InlineData("Location", "Location")]
    [InlineData("Forwarded", "Forwarded")]
    [InlineData("Keep-Alive", "Keep-Alive")]
    [InlineData("Set-Cookie", "Set-Cookie")]
    [InlineData("User-Agent", "User-Agent")]
    [InlineData("Retry-After", "Retry-After")]
    [InlineData("Set-Cookie2", "Set-Cookie2")]
    [InlineData("Content-Type", "Content-Type")]
    [InlineData("Max-Forwards", "Max-Forwards")]
    [InlineData("X-Request-Id", "X-Request-Id")]
    [InlineData("Cache-Control", "Cache-Control")]
    [InlineData("Content-Range", "Content-Range")]
    [InlineData("Last-Modified", "Last-Modified")]
    [InlineData("If-None-Match", "If-None-Match")]
    [InlineData("Accept-Charset", "Accept-Charset")]
    [InlineData("Accept-Ranges", "Accept-Ranges")]
    [InlineData("Content-Length", "Content-Length")]
    [InlineData("Accept-Encoding", "Accept-Encoding")]
    [InlineData("Accept-Language", "Accept-Language")]
    [InlineData("X-Forwarded-For", "X-Forwarded-For")]
    [InlineData("Content-Encoding", "Content-Encoding")]
    [InlineData("Content-Language", "Content-Language")]
    [InlineData("Content-Location", "Content-Location")]
    [InlineData("WWW-Authenticate", "WWW-Authenticate")]
    [InlineData("If-Modified-Since", "If-Modified-Since")]
    [InlineData("Transfer-Encoding", "Transfer-Encoding")]
    [InlineData("X-Forwarded-Proto", "X-Forwarded-Proto")]
    [InlineData("Proxy-Authenticate", "Proxy-Authenticate")]
    [InlineData("If-Unmodified-Since", "If-Unmodified-Since")]
    [InlineData("Proxy-Authorization", "Proxy-Authorization")]
    [InlineData("Strict-Transport-Security", "Strict-Transport-Security")]
    public void GetOrCreateHeaderName_should_intern_all_known_names(string input, string expected)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(input);
        var result = WellKnownHeaders.GetOrCreateHeaderName(bytes);
        Assert.Equal(expected, result.Name);
    }

    [Theory(Timeout = 5000)]
    [InlineData("X")]
    [InlineData("ABC")]
    [InlineData("Nope")]
    [InlineData("XXXXX")]
    [InlineData("Random")]
    [InlineData("Unknown")]
    [InlineData("BadMatch")]
    [InlineData("NotAMatch")]
    [InlineData("SomeHeader")]
    [InlineData("CustomValue")]
    [InlineData("WrongHeader!")]
    [InlineData("NotCacheCtrl")]
    [InlineData("NotContentLen")]
    [InlineData("NotContentEnco")]
    [InlineData("NotTransferEnco")]
    [InlineData("SixteenCharName!")]
    [InlineData("NotTransferEncode")]
    [InlineData("EighteenCharHeader")]
    [InlineData("NineteenCharHeader!")]
    [InlineData("X-Very-Long-Custom-Header-Name")]
    public void GetOrCreateHeaderName_should_allocate_for_unknown_names(string input)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(input);
        var result = WellKnownHeaders.GetOrCreateHeaderName(bytes);
        Assert.Equal(input, result.Name);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateHeaderName_should_handle_empty_span()
    {
        var result = WellKnownHeaders.GetOrCreateHeaderName([]);
        Assert.Equal("", result.Name);
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_match_same_case()
    {
        Assert.True(WellKnownHeaders.EqualsIgnoreCase("Host"u8, "Host"u8));
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_match_different_case()
    {
        Assert.True(WellKnownHeaders.EqualsIgnoreCase("HOST"u8, "host"u8));
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_not_match_different_length()
    {
        Assert.False(WellKnownHeaders.EqualsIgnoreCase("Host"u8, "Hosts"u8));
    }

    [Fact(Timeout = 5000)]
    public void EqualsIgnoreCase_should_not_match_different_content()
    {
        Assert.False(WellKnownHeaders.EqualsIgnoreCase("Host"u8, "Hose"u8));
    }
}