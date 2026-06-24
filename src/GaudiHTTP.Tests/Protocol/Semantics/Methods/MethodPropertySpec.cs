using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Tests.Protocol.Semantics.Methods;

public sealed class MethodPropertySpec
{
    [Theory(Timeout = 5000)]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [Trait("RFC", "RFC9110-9.2.1")]
    public void MethodProperty_should_classify_GET_HEAD_OPTIONS_TRACE_as_safe(string methodName)
    {
        var method = new HttpMethod(methodName);
        var isSafe = MethodProperties.IsSafe(method);

        Assert.True(isSafe);
    }

    [Theory(Timeout = 5000)]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("CONNECT")]
    [InlineData("PATCH")]
    [Trait("RFC", "RFC9110-9.2.1")]
    public void MethodProperty_should_classify_POST_PUT_DELETE_CONNECT_PATCH_as_unsafe(string methodName)
    {
        var method = new HttpMethod(methodName);
        var isSafe = MethodProperties.IsSafe(method);

        Assert.False(isSafe);
    }

    [Theory(Timeout = 5000)]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void MethodProperty_should_classify_PUT_DELETE_and_safe_methods_as_idempotent(string methodName)
    {
        var method = new HttpMethod(methodName);
        var isIdempotent = MethodProperties.IsIdempotent(method);

        Assert.True(isIdempotent);
    }

    [Theory(Timeout = 5000)]
    [InlineData("POST")]
    [InlineData("PATCH")]
    [InlineData("CONNECT")]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void MethodProperty_should_classify_POST_PATCH_CONNECT_as_non_idempotent(string methodName)
    {
        var method = new HttpMethod(methodName);
        var isIdempotent = MethodProperties.IsIdempotent(method);

        Assert.False(isIdempotent);
    }

    [Theory(Timeout = 5000)]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("POST")]
    [Trait("RFC", "RFC9110-9.2.3")]
    public void MethodProperty_should_classify_GET_HEAD_POST_as_cacheable(string methodName)
    {
        var method = new HttpMethod(methodName);
        var isCacheable = MethodProperties.IsCacheable(method);

        Assert.True(isCacheable);
    }

    [Theory(Timeout = 5000)]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("CONNECT")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [InlineData("PATCH")]
    [Trait("RFC", "RFC9110-9.2.3")]
    public void MethodProperty_should_classify_PUT_DELETE_CONNECT_OPTIONS_TRACE_PATCH_as_non_cacheable(string methodName)
    {
        var method = new HttpMethod(methodName);
        var isCacheable = MethodProperties.IsCacheable(method);

        Assert.False(isCacheable);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void MethodProperty_should_correctly_identify_safe_as_subset_of_idempotent()
    {
        var safeMethods = new[] { "GET", "HEAD", "OPTIONS", "TRACE" };

        foreach (var methodName in safeMethods)
        {
            var method = new HttpMethod(methodName);
            Assert.True(MethodProperties.IsSafe(method));
            Assert.True(MethodProperties.IsIdempotent(method));
        }
    }

    [Fact(Timeout = 5000)]
    public void MethodProperty_should_handle_custom_methods()
    {
        var customMethod = new HttpMethod("CUSTOM");

        var isSafe = MethodProperties.IsSafe(customMethod);
        var isIdempotent = MethodProperties.IsIdempotent(customMethod);
        var isCacheable = MethodProperties.IsCacheable(customMethod);

        Assert.False(isSafe);
        Assert.False(isIdempotent);
        Assert.False(isCacheable);
    }

    [Fact(Timeout = 5000)]
    public void MethodProperty_should_be_case_sensitive()
    {
        var uppercaseGet = new HttpMethod("GET");
        var lowercaseGet = new HttpMethod("get");

        var uppercaseIsSafe = MethodProperties.IsSafe(uppercaseGet);
        var lowercaseIsSafe = MethodProperties.IsSafe(lowercaseGet);

        Assert.True(uppercaseIsSafe);
        Assert.False(lowercaseIsSafe);
    }
}
