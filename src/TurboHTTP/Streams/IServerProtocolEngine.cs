using Akka;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams;

internal interface IServerProtocolEngine
{
    BidiFlow<ITransportInbound, IFeatureCollection, IFeatureCollection, ITransportOutbound, NotUsed> CreateFlow(
        IServiceProvider? services = null);
}

