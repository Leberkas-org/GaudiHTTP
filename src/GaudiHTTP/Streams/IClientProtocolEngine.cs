using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;

namespace GaudiHTTP.Streams;

internal interface IClientProtocolEngine
{
    BidiFlow<
        HttpRequestMessage,
        ITransportOutbound,
        ITransportInbound,
        HttpResponseMessage,
        NotUsed> CreateFlow();
}
