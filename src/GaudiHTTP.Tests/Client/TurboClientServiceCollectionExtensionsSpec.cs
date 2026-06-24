using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using GaudiHTTP.Client;

namespace GaudiHTTP.Tests.Client;

public sealed class TurboClientServiceCollectionExtensionsSpec
{
    [Fact(Timeout = 5000)]
    public void AddGaudiHttpClient_WithName_ReturnsBuilder()
    {
        var services = new ServiceCollection();

        var builder = services.AddGaudiHttpClient("test");

        Assert.NotNull(builder);
        Assert.IsAssignableFrom<IGaudiHttpClientBuilder>(builder);
    }

    [Fact(Timeout = 5000)]
    public void AddGaudiHttpClient_WithName_RegistersOptionsMonitor()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test");

        // Verify that builder was returned and configuration would work
        var sp = services.BuildServiceProvider();
        var monitor = sp.GetService<IOptionsMonitor<TurboClientOptions>>();
        Assert.NotNull(monitor);
    }

    [Fact(Timeout = 5000)]
    public void AddGaudiHttpClient_WithNameAndConfigure_RegistersConfiguration()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test", opt => opt.ConnectTimeout = TimeSpan.FromSeconds(30));

        var sp = services.BuildServiceProvider();
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var options = optionsMonitor.Get("test");

        Assert.Equal(TimeSpan.FromSeconds(30), options.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void AddGaudiHttpClient_DefaultName_UsesEmptyString()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient();

        var sp = services.BuildServiceProvider();
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var options = optionsMonitor.Get(string.Empty);

        Assert.NotNull(options);
    }

    [Fact(Timeout = 5000)]
    public void AddGaudiHttpClient_MultipleClients_ConfiguresEachSeparately()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test1", opt => opt.ConnectTimeout = TimeSpan.FromSeconds(10));
        services.AddGaudiHttpClient("test2", opt => opt.ConnectTimeout = TimeSpan.FromSeconds(20));

        var sp = services.BuildServiceProvider();
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var options1 = optionsMonitor.Get("test1");
        var options2 = optionsMonitor.Get("test2");

        Assert.Equal(TimeSpan.FromSeconds(10), options1.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(20), options2.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void AddGaudiHttpClient_WithoutConfigure_AllowsBuilderConfiguration()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test")
            .WithCookies();

        var sp = services.BuildServiceProvider();
        var descriptorMonitor = sp.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>();
        var descriptor = descriptorMonitor.Get("test");

        Assert.True(descriptor.EnableCookies);
    }

    [Fact(Timeout = 5000)]
    public void AddGaudiHttpClient_TypedClient_ReturnsBuilder()
    {
        var services = new ServiceCollection();

        var builder = services.AddGaudiHttpClient<TestClient>();

        Assert.NotNull(builder);
        Assert.IsAssignableFrom<IGaudiHttpClientBuilder>(builder);
    }

    [Fact(Timeout = 5000)]
    public void AddGaudiHttpClient_TypedClient_UsesTypeNameAsClientName()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient<TestClient>(opt => opt.ConnectTimeout = TimeSpan.FromSeconds(25));

        var sp = services.BuildServiceProvider();
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var options = optionsMonitor.Get("TestClient");

        Assert.Equal(TimeSpan.FromSeconds(25), options.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void AddGaudiHttpClient_TypedClientInterface_ReturnsBuilder()
    {
        var services = new ServiceCollection();

        var builder = services.AddGaudiHttpClient<ITestClient, TestClientImpl>();

        Assert.NotNull(builder);
        Assert.IsAssignableFrom<IGaudiHttpClientBuilder>(builder);
    }

    [Fact(Timeout = 5000)]
    public void AddGaudiHttpClient_TypedClientInterface_UsesInterfaceNameAsClientName()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient<ITestClient, TestClientImpl>(opt => opt.ConnectTimeout = TimeSpan.FromSeconds(35));

        var sp = services.BuildServiceProvider();
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var options = optionsMonitor.Get("ITestClient");

        Assert.Equal(TimeSpan.FromSeconds(35), options.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void CreateClient_WithNullFactory_ThrowsArgumentNullException()
    {
        IGaudiHttpClientFactory? nullFactory = null;

        Assert.Throws<ArgumentNullException>(() => nullFactory!.CreateClient());
    }

    [Fact(Timeout = 5000)]
    public void AddGaudiHttpClient_MultipleExtensions_AllConfigured()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("client1", opt => opt.MaxConcurrentEndpoints = 100);
        services.AddGaudiHttpClient("client2", opt => opt.MaxConcurrentEndpoints = 200);

        var sp = services.BuildServiceProvider();
        var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
        var options1 = optionsMonitor.Get("client1");
        var options2 = optionsMonitor.Get("client2");

        Assert.Equal(100u, options1.MaxConcurrentEndpoints);
        Assert.Equal(200u, options2.MaxConcurrentEndpoints);
    }

    [Fact(Timeout = 5000)]
    public void AddGaudiHttpClient_RegistersGaudiHttpClientName()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient("test");

        var sp = services.BuildServiceProvider();
        var names = sp.GetServices<GaudiHttpClientName>();

        Assert.Contains(names, n => n.Name == "test");
    }

    [Fact(Timeout = 5000)]
    public void AddGaudiHttpClient_WithDefaultName_RegistersEmptyName()
    {
        var services = new ServiceCollection();
        services.AddGaudiHttpClient();

        var sp = services.BuildServiceProvider();
        var names = sp.GetServices<GaudiHttpClientName>();

        Assert.Contains(names, n => n.Name == string.Empty);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpClientName_RecordType_HasName()
    {
        var name = new GaudiHttpClientName("test");

        Assert.Equal("test", name.Name);
    }

    [Fact(Timeout = 5000)]
    public void GaudiHttpClientName_Equality_ComparesName()
    {
        var name1 = new GaudiHttpClientName("test");
        var name2 = new GaudiHttpClientName("test");
        var name3 = new GaudiHttpClientName("other");

        Assert.Equal(name1, name2);
        Assert.NotEqual(name1, name3);
    }

    private sealed class TestClient;

    private interface ITestClient;

    private sealed class TestClientImpl : ITestClient;
}