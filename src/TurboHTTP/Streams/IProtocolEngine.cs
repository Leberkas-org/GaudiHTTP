using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;

namespace TurboHTTP.Streams;

internal interface IHttpProtocolEngine
{
    BidiFlow<
        HttpRequestMessage,
        ITransportOutbound,
        ITransportInbound,
        HttpResponseMessage,
        NotUsed> CreateFlow();
}
