using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using GaudiHTTP.Client;

namespace GaudiHTTP.Tests.Client.Hosting;

public sealed class GaudiHttpClientBuilderHandlerSpec
{
    private sealed class TestHandler : GaudiHandler;

    private sealed class AlphaHandler : GaudiHandler;

    private sealed class BetaHandler : GaudiHandler;

    private static GaudiClientDescriptor GetDescriptor(IServiceCollection services, string name)
    {
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<GaudiClientDescriptor>>().Get(name);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpClientBuilderHandler_should_add_type_to_handler_types()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").AddHandler<TestHandler>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Contains(typeof(TestHandler), descriptor.HandlerTypes);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpClientBuilderHandler_should_add_factory_to_handler_factories()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").AddHandler<TestHandler>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Single(descriptor.HandlerFactories);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpClientBuilderHandler_should_register_transient_service()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").AddHandler<TestHandler>();

        Assert.Contains(services, sd =>
            sd.ServiceType == typeof(TestHandler) &&
            sd.Lifetime == ServiceLifetime.Transient);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpClientBuilderHandler_should_add_one_factory_with_no_type_entry()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").UseRequest(req => req);

        var descriptor = GetDescriptor(services, "test");

        Assert.Single(descriptor.HandlerFactories);
        Assert.Empty(descriptor.HandlerTypes);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpClientBuilderHandler_should_preserve_fifo_order_in_types()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test")
            .AddHandler<AlphaHandler>()
            .AddHandler<BetaHandler>();

        var descriptor = GetDescriptor(services, "test");

        Assert.Equal(typeof(AlphaHandler), descriptor.HandlerTypes[0]);
        Assert.Equal(typeof(BetaHandler), descriptor.HandlerTypes[1]);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpClientBuilderHandler_should_preserve_fifo_order_in_factories()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test")
            .AddHandler<AlphaHandler>()
            .AddHandler<BetaHandler>();

        var sp = services.BuildServiceProvider();
        var descriptor = sp.GetRequiredService<IOptionsMonitor<GaudiClientDescriptor>>().Get("test");

        Assert.Equal(2, descriptor.HandlerFactories.Count);
        Assert.IsType<AlphaHandler>(descriptor.HandlerFactories[0](sp));
        Assert.IsType<BetaHandler>(descriptor.HandlerFactories[1](sp));
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpClientBuilderHandler_should_resolve_from_service_provider()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test").AddHandler<TestHandler>();

        var sp = services.BuildServiceProvider();
        var descriptor = sp.GetRequiredService<IOptionsMonitor<GaudiClientDescriptor>>().Get("test");

        var resolved = descriptor.HandlerFactories[0](sp);

        Assert.IsType<TestHandler>(resolved);
    }
}