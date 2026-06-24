using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using GaudiHTTP.Client;
using GaudiHTTP.Features.Caching;
using GaudiHTTP.Features.Cookies;

namespace GaudiHTTP.Tests.Client;

public sealed class GaudiHttpClientBuilderExtensionsSpec
{
    private static TurboClientDescriptor GetDescriptor(IServiceCollection services, string name)
    {
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>().Get(name);
    }

    [Fact(Timeout = 5000)]
    public void WithCookies_NoJar_SetsEnableCookiesTrue()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").WithCookies();

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.EnableCookies);
        Assert.NotNull(descriptor.CustomCookieJar);
    }

    [Fact(Timeout = 5000)]
    public void WithCookies_WithStore_SetsCustomCookieJar()
    {
        var store = new MemoryCookieStore();
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").WithCookies(store);

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.EnableCookies);
        Assert.NotNull(descriptor.CustomCookieJar);
    }

    [Fact(Timeout = 5000)]
    public void WithCookies_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddGaudiHttpClient("test");

        var result = builder.WithCookies();

        Assert.Same(builder, result);
    }

    [Fact(Timeout = 5000)]
    public void WithCache_NoStore_AssignsCachePolicy()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").WithCache(x => x.MaxEntries = 500);

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(500, descriptor.CachePolicy?.MaxEntries);
        Assert.Null(descriptor.CustomCacheStore);
    }

    [Fact(Timeout = 5000)]
    public void WithCache_NoConfiguration_CreatesDefaultCachePolicy()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").WithCache();

        var descriptor = GetDescriptor(services, "test");

        Assert.NotNull(descriptor.CachePolicy);
    }

    [Fact(Timeout = 5000)]
    public void WithCache_WithStore_AssignsCacheStoreAndPolicy()
    {
        var services = new ServiceCollection();
        var customStore = new MemoryCacheStore();
        services.AddGaudiHttpClient("test").WithCache(customStore, x => x.MaxEntries = 100);

        var descriptor = GetDescriptor(services, "test");

        Assert.NotNull(descriptor.CachePolicy);
        Assert.Same(customStore, descriptor.CustomCacheStore);
    }

    [Fact(Timeout = 5000)]
    public void WithCache_WithStoreNoConfiguration_UsesDef()
    {
        var services = new ServiceCollection();
        var customStore = new MemoryCacheStore();
        services.AddGaudiHttpClient("test").WithCache(customStore);

        var descriptor = GetDescriptor(services, "test");

        Assert.NotNull(descriptor.CachePolicy);
        Assert.Same(customStore, descriptor.CustomCacheStore);
    }

    [Fact(Timeout = 5000)]
    public void WithCache_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddGaudiHttpClient("test");

        var result = builder.WithCache();

        Assert.Same(builder, result);
    }

    [Fact(Timeout = 5000)]
    public void WithRetry_NoPolicy_CreatesDefaultRetryPolicy()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").WithRetry();

        var descriptor = GetDescriptor(services, "test");

        Assert.NotNull(descriptor.RetryPolicy);
    }

    [Fact(Timeout = 5000)]
    public void WithRetry_WithConfiguration_AssignsRetryPolicy()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").WithRetry(x => x.MaxRetries = 5);

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(5, descriptor.RetryPolicy?.MaxRetries);
    }

    [Fact(Timeout = 5000)]
    public void WithRetry_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddGaudiHttpClient("test");

        var result = builder.WithRetry();

        Assert.Same(builder, result);
    }

    [Fact(Timeout = 5000)]
    public void WithRedirect_NoPolicy_SetsDefaultPolicy()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").WithRedirect();

        var descriptor = GetDescriptor(services, "test");

        Assert.NotNull(descriptor.RedirectPolicy);
    }

    [Fact(Timeout = 5000)]
    public void WithRedirect_WithPolicy_AssignsRedirectPolicy()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").WithRedirect(x => x.MaxRedirects = 5);

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(5, descriptor.RedirectPolicy?.MaxRedirects);
    }

    [Fact(Timeout = 5000)]
    public void WithRedirect_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddGaudiHttpClient("test");

        var result = builder.WithRedirect();

        Assert.Same(builder, result);
    }

    [Fact(Timeout = 5000)]
    public void WithDecompression_NoArg_SetsTrue()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").WithDecompression();

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.AutomaticDecompression);
    }

    [Fact(Timeout = 5000)]
    public void WithDecompression_True_SetsTrue()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").WithDecompression();

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.AutomaticDecompression);
    }

    [Fact(Timeout = 5000)]
    public void WithDecompression_False_SetsFalse()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").WithDecompression(false);

        var descriptor = GetDescriptor(services, "test");

        Assert.False(descriptor.AutomaticDecompression);
    }

    [Fact(Timeout = 5000)]
    public void WithDecompression_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddGaudiHttpClient("test");

        var result = builder.WithDecompression();

        Assert.Same(builder, result);
    }

    [Fact(Timeout = 5000)]
    public void WithRequestCompression_NoPolicy_CreatesDefaultPolicy()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").WithRequestCompression();

        var descriptor = GetDescriptor(services, "test");

        Assert.NotNull(descriptor.CompressionPolicy);
    }

    [Fact(Timeout = 5000)]
    public void WithRequestCompression_WithPolicy_AssignsCompressionPolicy()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").WithRequestCompression(x => x.MinBodySize = 1024);

        var descriptor = GetDescriptor(services, "test");

        Assert.NotNull(descriptor.CompressionPolicy);
        Assert.Equal(1024, descriptor.CompressionPolicy?.MinBodySizeBytes);
    }

    [Fact(Timeout = 5000)]
    public void WithRequestCompression_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddGaudiHttpClient("test");

        var result = builder.WithRequestCompression();

        Assert.Same(builder, result);
    }

    [Fact(Timeout = 5000)]
    public void WithExpectContinue_NoPolicy_CreatesDefaultPolicy()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").WithExpectContinue();

        var descriptor = GetDescriptor(services, "test");

        Assert.NotNull(descriptor.Expect100Policy);
    }

    [Fact(Timeout = 5000)]
    public void WithExpectContinue_WithPolicy_AssignsPolicy()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").WithExpectContinue(x => x.MinBodySize = 2048);

        var descriptor = GetDescriptor(services, "test");

        Assert.NotNull(descriptor.Expect100Policy);
        Assert.Equal(2048, descriptor.Expect100Policy?.MinBodySizeBytes);
    }

    [Fact(Timeout = 5000)]
    public void WithExpectContinue_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddGaudiHttpClient("test");

        var result = builder.WithExpectContinue();

        Assert.Same(builder, result);
    }

    [Fact(Timeout = 5000)]
    public void AddHandler_RegistersHandlerType()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").AddHandler<TestHandler>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Contains(typeof(TestHandler), descriptor.HandlerTypes);
    }

    [Fact(Timeout = 5000)]
    public void AddHandler_RegistersHandlerFactory()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").AddHandler<TestHandler>();

        var descriptor = GetDescriptor(services, "test");

        Assert.NotEmpty(descriptor.HandlerFactories);
    }

    [Fact(Timeout = 5000)]
    public void AddHandler_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddGaudiHttpClient("test");

        var result = builder.AddHandler<TestHandler>();

        Assert.Same(builder, result);
    }

    [Fact(Timeout = 5000)]
    public void AddHandler_MultipleHandlers_PreservesOrder()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test")
            .AddHandler<TestHandler>()
            .AddHandler<AnotherTestHandler>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(2, descriptor.HandlerTypes.Count);
        Assert.Equal(typeof(TestHandler), descriptor.HandlerTypes[0]);
        Assert.Equal(typeof(AnotherTestHandler), descriptor.HandlerTypes[1]);
    }

    [Fact(Timeout = 5000)]
    public void UseRequest_RegistersRequestTransform()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test")
            .UseRequest(r => r);

        var descriptor = GetDescriptor(services, "test");

        Assert.NotEmpty(descriptor.HandlerFactories);
    }

    [Fact(Timeout = 5000)]
    public void UseRequest_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddGaudiHttpClient("test");

        var result = builder.UseRequest(r => r);

        Assert.Same(builder, result);
    }

    [Fact(Timeout = 5000)]
    public void UseRequest_TransformIsAppliedToRequest()
    {
        var services = new ServiceCollection();
        var originalRequest = new HttpRequestMessage();
        var transformedRequest = new HttpRequestMessage();

        services.AddGaudiHttpClient("test")
            .UseRequest(_ => transformedRequest);

        var descriptor = GetDescriptor(services, "test");
        var sp = services.BuildServiceProvider();

        var factory = descriptor.HandlerFactories[0];
        var handler = factory(sp);

        var result = handler.ProcessRequest(originalRequest);

        Assert.Same(transformedRequest, result);
    }

    [Fact(Timeout = 5000)]
    public void UseResponse_RegistersResponseTransform()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test")
            .UseResponse((_, r) => r);

        var descriptor = GetDescriptor(services, "test");

        Assert.NotEmpty(descriptor.HandlerFactories);
    }

    [Fact(Timeout = 5000)]
    public void UseResponse_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddGaudiHttpClient("test");

        var result = builder.UseResponse((_, r) => r);

        Assert.Same(builder, result);
    }

    [Fact(Timeout = 5000)]
    public void UseResponse_TransformIsAppliedToResponse()
    {
        var services = new ServiceCollection();
        var originalRequest = new HttpRequestMessage();
        var originalResponse = new HttpResponseMessage();
        var transformedResponse = new HttpResponseMessage();

        services.AddGaudiHttpClient("test")
            .UseResponse((_, _) => transformedResponse);

        var descriptor = GetDescriptor(services, "test");
        var sp = services.BuildServiceProvider();

        var factory = descriptor.HandlerFactories[0];
        var handler = factory(sp);

        var result = handler.ProcessResponse(originalRequest, originalResponse);

        Assert.Same(transformedResponse, result);
    }

    [Fact(Timeout = 5000)]
    public void ChainedConfiguration_AppliesAllSettings()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test")
            .WithCookies()
            .WithCache()
            .WithRetry(x => x.MaxRetries = 3)
            .WithRedirect(x => x.MaxRedirects = 5)
            .WithDecompression(false)
            .WithRequestCompression()
            .WithExpectContinue();

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.EnableCookies);
        Assert.NotNull(descriptor.CachePolicy);
        Assert.Equal(3, descriptor.RetryPolicy?.MaxRetries);
        Assert.Equal(5, descriptor.RedirectPolicy?.MaxRedirects);
        Assert.False(descriptor.AutomaticDecompression);
        Assert.NotNull(descriptor.CompressionPolicy);
        Assert.NotNull(descriptor.Expect100Policy);
    }

    [Fact(Timeout = 5000)]
    public void DefaultDescriptor_HasDefaultValues()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test");

        var descriptor = GetDescriptor(services, "test");

        Assert.False(descriptor.EnableCookies);
        Assert.Null(descriptor.CachePolicy);
        Assert.True(descriptor.AutomaticDecompression);
    }

    private sealed class TestHandler : TurboHandler
    {
        public override HttpRequestMessage ProcessRequest(HttpRequestMessage request) => request;
    }

    private sealed class AnotherTestHandler : TurboHandler
    {
        public override HttpRequestMessage ProcessRequest(HttpRequestMessage request) => request;
    }
}