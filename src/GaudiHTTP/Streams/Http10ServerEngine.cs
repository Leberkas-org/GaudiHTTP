using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Server;
using GaudiHTTP.Streams.Stages.Server;

namespace GaudiHTTP.Streams;

internal sealed class Http10ServerEngine(GaudiServerOptions options) : IServerProtocolEngine
{
    public Version ProtocolVersion => new(1, 0);

    public BidiFlow<ITransportInbound, IFeatureCollection, IFeatureCollection, ITransportOutbound, NotUsed> CreateFlow(IServiceProvider? services = null)
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http10ServerConnectionStage(options, services));

            return new BidiShape<
                ITransportInbound,
                IFeatureCollection,
                IFeatureCollection,
                ITransportOutbound>(
                connection.InNetwork,
                connection.OutRequest,
                connection.InResponse,
                connection.OutNetwork);
        }));
    }
}
