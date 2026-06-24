using Microsoft.Extensions.DependencyInjection;
using GaudiHTTP.Client;

namespace GaudiHTTP.Tests.Client;

public sealed class TypedClientRegistrationSpec
{
    private sealed class MyApiClient(IGaudiHttpClient client)
    {
        public IGaudiHttpClient Client { get; } = client;
    }

    private interface IMyService
    {
        IGaudiHttpClient Client { get; }
    }

    private sealed class MyService(IGaudiHttpClient client) : IMyService
    {
        public IGaudiHttpClient Client { get; } = client;
    }

    [Fact(Timeout = 10000)]
    public void AddGaudiHttpClient_typed_should_resolve_POCO_client()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient<MyApiClient>();
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<MyApiClient>();

        Assert.NotNull(client);
        Assert.NotNull(client.Client);
    }

    [Fact(Timeout = 10000)]
    public void AddGaudiHttpClient_typed_with_interface_should_resolve_via_interface()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient<IMyService, MyService>();
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<IMyService>();

        Assert.NotNull(client);
        Assert.NotNull(client.Client);
    }

    [Fact(Timeout = 10000)]
    public void AddGaudiHttpClient_typed_with_interface_should_resolve_impl_directly()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient<IMyService, MyService>();
        using var sp = services.BuildServiceProvider();

        var impl = sp.GetRequiredService<MyService>();

        Assert.NotNull(impl);
        Assert.NotNull(impl.Client);
    }
}
