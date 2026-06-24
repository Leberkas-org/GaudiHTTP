using GaudiHTTP.Client;
using GaudiHTTP.Streams;
using Xunit;

namespace GaudiHTTP.Tests.Shared;

public abstract class ClientAcceptanceTestBase : AcceptanceTestBase
{
    protected static async Task<HttpResponseMessage> SendClientAsync(
        Version version,
        HttpRequestMessage request,
        Func<int, byte[], byte[]?> responseFactory,
        Action<IGaudiHttpClientBuilder>? configure = null,
        Action<GaudiClientOptions>? configureOptions = null)
    {
        var stage = CreateScriptedConnection(responseFactory);
        var transports = new TransportRegistry()
            .Register(version, stage.AsFlow());

        await using var helper = ClientAcceptanceHelper.Create(
            transports, version, configure, configureOptions);

        return await helper.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}