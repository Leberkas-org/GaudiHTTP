using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace TurboHTTP.IntegrationTests.Client.Shared;

internal sealed class HttpbinContainer : IAsyncDisposable
{
    private const int InternalPort = 8080;
    public const string ContainerName = "turbohttp-httpbin";
    public const string NetworkAlias = "httpbin";

    private IContainer? _container;

    public int HttpPort { get; private set; }

    public async Task StartAsync(INetwork network)
    {
        _container = new ContainerBuilder("mccutchen/go-httpbin:2.22.1")
            .WithName(ContainerName)
            .WithNetwork(network)
            .WithNetworkAliases(NetworkAlias)
            .WithPortBinding(InternalPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPort(InternalPort)
                    .ForPath("/get")))
            .Build();

        await _container.StartAsync();
        HttpPort = _container.GetMappedPublicPort(InternalPort);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}