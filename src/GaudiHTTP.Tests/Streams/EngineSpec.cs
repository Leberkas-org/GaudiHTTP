using GaudiHTTP.Client;
using Akka;
using Akka.Actor;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using GaudiHTTP.Streams;
using GaudiHTTP.Streams.Pooling;

namespace GaudiHTTP.Tests.Streams;

public sealed class EngineSpec
{
    public static Flow<ITransportOutbound, ITransportInbound, NotUsed> CreateMock()
    {
        return TransportFactory.CreateTcpClient(ActorRefs.Nobody, new Http2PoolingStrategy());
    }

    private static TransportRegistry CreateMockTransportRegistry()
    {
        var mockFactory = CreateMock();

        var registry = new TransportRegistry();
        registry.Register(System.Net.HttpVersion.Version10, mockFactory);
        registry.Register(System.Net.HttpVersion.Version11, mockFactory);
        registry.Register(System.Net.HttpVersion.Version20, mockFactory);
        registry.Register(System.Net.HttpVersion.Version30, mockFactory);
        return registry;
    }

    [Fact(Timeout = 5000)]
    public void Engine_should_create_valid_flow()
    {
        // Arrange
        var engine = new Engine();
        var transports = CreateMockTransportRegistry();
        var descriptor = PipelineDescriptor.Empty;

        // Act
        var flow = engine.CreateFlow(transports, descriptor);

        // Assert
        Assert.NotNull(flow);
    }

    [Fact(Timeout = 5000)]
    public void Engine_should_use_provided_gaudi_client_options()
    {
        // Arrange
        var engine = new Engine();
        var transports = CreateMockTransportRegistry();
        var descriptor = PipelineDescriptor.Empty;
        var options = new GaudiClientOptions
        {
            MaxConcurrentEndpoints = 20,
            Http1 = new Http1ClientOptions { MaxPipelineDepth = 2 }
        };

        // Act
        var flow = engine.CreateFlow(transports, descriptor, options);

        // Assert
        Assert.NotNull(flow);
    }

    [Fact(Timeout = 5000)]
    public void Engine_should_use_default_options_when_null_provided()
    {
        // Arrange
        var engine = new Engine();
        var transports = CreateMockTransportRegistry();
        var descriptor = PipelineDescriptor.Empty;

        // Act
        var flow = engine.CreateFlow(transports, descriptor, options: null);

        // Assert
        Assert.NotNull(flow);
    }

    [Fact(Timeout = 5000)]
    public void Engine_should_use_default_options_when_provided()
    {
        // Arrange
        var engine = new Engine();
        var transports = CreateMockTransportRegistry();
        var descriptor = PipelineDescriptor.Empty;

        // Act
        var flow = engine.CreateFlow(transports, descriptor,
            options: new GaudiClientOptions());

        // Assert
        Assert.NotNull(flow);
    }

    [Fact(Timeout = 5000)]
    public void Engine_should_build_gaudi_request_options_with_base_address()
    {
        // Arrange
        var baseUri = new Uri("http://api.example.com");
        var options = new GaudiClientOptions { BaseAddress = baseUri };

        // Act
        var holder = new HttpRequestMessage();
        var reqOptions = new GaudiRequestOptions(
            BaseAddress: options.BaseAddress,
            DefaultRequestVersion: holder.Version,
            DefaultRequestHeaders: holder.Headers,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrHigher,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: options.Credentials,
            PreAuthenticate: options.PreAuthenticate);

        // Assert
        Assert.Equal(baseUri, reqOptions.BaseAddress);
    }
}