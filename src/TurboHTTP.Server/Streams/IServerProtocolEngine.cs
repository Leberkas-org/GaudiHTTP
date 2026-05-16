using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;

namespace TurboHTTP.Server.Streams;

internal interface IServerProtocolEngine
{
    BidiFlow<ITransportInbound, HttpRequestMessage, HttpResponseMessage, ITransportOutbound, NotUsed> CreateFlow();
}

