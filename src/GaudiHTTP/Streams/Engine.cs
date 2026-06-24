using GaudiHTTP.Client;
using Akka;
using Akka.Streams.Dsl;

namespace GaudiHTTP.Streams;

internal sealed class Engine
{
    internal Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(
        TransportRegistry transports,
        PipelineDescriptor descriptor,
        GaudiClientOptions? options = null)
    {
        options ??= new GaudiClientOptions();

        var engineFlow = ProtocolCoreBuilder.Build(options, transports);

        return FeaturePipelineBuilder.Build(engineFlow, descriptor);
    }
}
