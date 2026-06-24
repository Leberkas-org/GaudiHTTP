using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using GaudiHTTP.Client;
using GaudiHTTP.Features.Cookies;

namespace GaudiHTTP.Tests.Client.Hosting;

public sealed class NamedClientIsolationSpec
{
    private static GaudiClientDescriptor GetDescriptor(IServiceCollection services, string name)
    {
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<GaudiClientDescriptor>>().Get(name);
    }

    [Fact(Timeout = 5000)]
    public void NamedClientIsolation_should_have_independent_descriptor_instances()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("a");
        services.AddGaudiHttpClient("b");

        var sp = services.BuildServiceProvider();
        var monitor = sp.GetRequiredService<IOptionsMonitor<GaudiClientDescriptor>>();

        var descriptorA = monitor.Get("a");
        var descriptorB = monitor.Get("b");

        Assert.NotSame(descriptorA, descriptorB);
    }

    [Fact(Timeout = 5000)]
    public void NamedClientIsolation_should_have_separate_cookie_store_instances()
    {
        var storeA = new MemoryCookieStore();
        var storeB = new MemoryCookieStore();

        var services = new ServiceCollection();
        services.AddGaudiHttpClient("a").WithCookies(storeA);
        services.AddGaudiHttpClient("b").WithCookies(storeB);

        var descriptorA = GetDescriptor(services, "a");
        var descriptorB = GetDescriptor(services, "b");

        Assert.NotNull(descriptorA.CustomCookieJar);
        Assert.NotNull(descriptorB.CustomCookieJar);
        Assert.NotSame(descriptorA.CustomCookieJar, descriptorB.CustomCookieJar);
    }

    [Fact(Timeout = 5000)]
    public void NamedClientIsolation_should_not_affect_other_clients_when_configuring_cookies()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("a").WithCookies();
        services.AddGaudiHttpClient("b");

        var descriptorA = GetDescriptor(services, "a");
        var descriptorB = GetDescriptor(services, "b");

        Assert.True(descriptorA.EnableCookies);
        Assert.False(descriptorB.EnableCookies);
        Assert.Null(descriptorB.CustomCookieJar);
    }

    [Fact(Timeout = 5000)]
    public void NamedClientIsolation_should_be_independent_when_mixed_configuration()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("a").WithCookies();
        services.AddGaudiHttpClient("b");

        var descriptorA = GetDescriptor(services, "a");
        var descriptorB = GetDescriptor(services, "b");

        // "a" has cookies enabled
        Assert.True(descriptorA.EnableCookies);

        // "b" has no cookies and is otherwise unaffected
        Assert.False(descriptorB.EnableCookies);
        Assert.Null(descriptorB.CustomCookieJar);
        Assert.Empty(descriptorB.HandlerFactories);
    }
}