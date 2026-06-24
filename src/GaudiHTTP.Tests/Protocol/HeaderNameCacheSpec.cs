using System.Text;
using TurboHTTP.Protocol;

namespace TurboHTTP.Tests.Protocol;

public sealed class HeaderNameCacheSpec
{
    [Fact(Timeout = 5000)]
    public void GetOrAdd_should_return_well_known_header_on_first_call()
    {
        var cache = new HeaderNameCache();
        var result = cache.GetOrAdd("content-type"u8);
        Assert.Equal("content-type", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrAdd_should_return_same_string_instance_for_repeated_custom_header()
    {
        var cache = new HeaderNameCache();
        var first = cache.GetOrAdd("x-custom-trace-id"u8);
        var second = cache.GetOrAdd("x-custom-trace-id"u8);
        Assert.Same(first, second);
    }

    [Fact(Timeout = 5000)]
    public void GetOrAdd_should_decode_unknown_header_correctly()
    {
        var cache = new HeaderNameCache();
        var result = cache.GetOrAdd("x-amz-request-id"u8);
        Assert.Equal("x-amz-request-id", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrAdd_should_handle_different_headers_in_same_slot_via_overwrite()
    {
        var cache = new HeaderNameCache();
        // These may or may not collide — the test verifies correctness regardless
        var a = cache.GetOrAdd("x-header-alpha"u8);
        var b = cache.GetOrAdd("x-header-beta"u8);
        Assert.Equal("x-header-alpha", a);
        Assert.Equal("x-header-beta", b);
    }

    [Fact(Timeout = 5000)]
    public void GetOrAdd_should_return_interned_well_known_strings()
    {
        var cache = new HeaderNameCache();
        // WellKnownHeaders.TryResolve returns static string instances
        var result1 = cache.GetOrAdd("authorization"u8);
        var result2 = cache.GetOrAdd("authorization"u8);
        Assert.Same(result1, result2);
    }

    [Fact(Timeout = 5000)]
    public void GetOrAddAscii_should_return_same_instance_for_repeated_calls()
    {
        var cache = new HeaderNameCache();
        var first = cache.GetOrAddAscii("X-Custom-Header"u8);
        var second = cache.GetOrAddAscii("X-Custom-Header"u8);
        Assert.Same(first, second);
    }

    [Fact(Timeout = 5000)]
    public void GetOrAddAscii_should_prefer_well_known_headers()
    {
        var cache = new HeaderNameCache();
        var result = cache.GetOrAddAscii("gzip"u8);
        Assert.Equal("gzip", result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrAdd_should_handle_empty_span()
    {
        var cache = new HeaderNameCache();
        var result = cache.GetOrAdd(ReadOnlySpan<byte>.Empty);
        Assert.Equal(string.Empty, result);
    }

    [Fact(Timeout = 5000)]
    public void GetOrAdd_should_cache_multiple_distinct_headers()
    {
        var cache = new HeaderNameCache();
        var headers = new[]
        {
            "x-request-id", "x-correlation-id", "x-forwarded-host",
            "x-amz-date", "x-amz-security-token", "x-api-key"
        };

        foreach (var h in headers)
        {
            var bytes = Encoding.UTF8.GetBytes(h);
            cache.GetOrAdd(bytes);
        }

        // Second pass — should return cached instances
        foreach (var h in headers)
        {
            var bytes = Encoding.UTF8.GetBytes(h);
            var first = cache.GetOrAdd(bytes);
            var second = cache.GetOrAdd(bytes);
            Assert.Same(first, second);
        }
    }
}
