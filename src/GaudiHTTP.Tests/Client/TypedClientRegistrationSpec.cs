using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Client;

namespace TurboHTTP.Tests.Client;

public sealed class TypedClientRegistrationSpec
{
    private sealed class MyApiClient(ITurboHttpClient client)
    {
        public ITurboHttpClient Client { get; } = client;
    }

    private interface IMyService
    {
        ITurboHttpClient Client { get; }
    }

    private sealed class MyService(ITurboHttpClient client) : IMyService
    {
        public ITurboHttpClient Client { get; } = client;
    }

    [Fact(Timeout = 10000)]
    public void AddTurboHttpClient_typed_should_resolve_POCO_client()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient<MyApiClient>();
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<MyApiClient>();

        Assert.NotNull(client);
        Assert.NotNull(client.Client);
    }

    [Fact(Timeout = 10000)]
    public void AddTurboHttpClient_typed_with_interface_should_resolve_via_interface()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient<IMyService, MyService>();
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<IMyService>();

        Assert.NotNull(client);
        Assert.NotNull(client.Client);
    }

    [Fact(Timeout = 10000)]
    public void AddTurboHttpClient_typed_with_interface_should_resolve_impl_directly()
    {
        var services = new ServiceCollection();
        services.AddTurboHttpClient<IMyService, MyService>();
        using var sp = services.BuildServiceProvider();

        var impl = sp.GetRequiredService<MyService>();

        Assert.NotNull(impl);
        Assert.NotNull(impl.Client);
    }
}
