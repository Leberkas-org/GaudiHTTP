using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TurboHTTP.Protocol.Cookies;

namespace TurboHTTP.Tests.Hosting;

public sealed class TurboHttpClientBuilderFeatureTests
{
    private static TurboClientDescriptor GetDescriptor(IServiceCollection services, string name)
    {
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>().Get(name);
    }

    [Fact(DisplayName = "WithCookies() sets EnableCookies to true with no custom jar")]
    public void WithCookies_NoJar_SetsEnableCookiesTrue()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithCookies();

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.EnableCookies);
        Assert.NotNull(descriptor.CustomCookieJar);
    }

    [Fact(DisplayName = "WithCookies(jar) sets EnableCookies to true and assigns the custom jar")]
    public void WithCookies_WithJar_SetsCustomCookieJar()
    {
        var jar = new CookieJar();
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithCookies(jar);

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.EnableCookies);
        Assert.Same(jar, descriptor.CustomCookieJar);
    }

    [Fact(DisplayName = "WithCache(policy) assigns the cache policy to the descriptor")]
    public void WithCache_AssignsCachePolicy()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithCache(x => x.MaxEntries = 500);

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(500, descriptor.CachePolicy?.MaxEntries);
    }

    [Fact(DisplayName = "WithRetry(policy) assigns the retry policy to the descriptor")]
    public void WithRetry_AssignsRetryPolicy()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithRetry(x => x.MaxRetries = 5);

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(5, descriptor.RetryPolicy?.MaxRetries);
    }

    [Fact(DisplayName = "WithRedirect() sets a non-null default redirect policy")]
    public void WithRedirect_NoPolicy_SetsDefaultPolicy()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithRedirect();

        var descriptor = GetDescriptor(services, "test");

        Assert.NotNull(descriptor.RedirectPolicy);
    }

    [Fact(DisplayName = "WithRedirect(policy) assigns the provided redirect policy to the descriptor")]
    public void WithRedirect_WithPolicy_AssignsRedirectPolicy()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithRedirect(x => x.MaxRedirects = 5);

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(5, descriptor.RedirectPolicy?.MaxRedirects);
    }

    [Fact(DisplayName = "Default descriptor has AutomaticDecompression true")]
    public void Default_AutomaticDecompression_IsTrue()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test");

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.AutomaticDecompression);
    }

    [Fact(DisplayName = "WithDecompression() sets AutomaticDecompression to true")]
    public void WithDecompression_NoArg_SetsTrue()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithDecompression();

        var descriptor = GetDescriptor(services, "test");

        Assert.True(descriptor.AutomaticDecompression);
    }

    [Fact(DisplayName = "WithDecompression(false) sets AutomaticDecompression to false")]
    public void WithDecompression_False_SetsFalse()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient("test").WithDecompression(false);

        var descriptor = GetDescriptor(services, "test");

        Assert.False(descriptor.AutomaticDecompression);
    }
}