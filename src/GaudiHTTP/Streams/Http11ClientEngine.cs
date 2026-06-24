using GaudiHTTP.Client;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using GaudiHTTP.Streams.Stages.Client;

namespace GaudiHTTP.Streams;

internal class Http11ClientEngine(TurboClientOptions options) : IClientProtocolEngine
{
    public BidiFlow<HttpRequestMessage, ITransportOutbound, ITransportInbound, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var connection = b.Add(new Http11ClientConnectionStage(options));

            return new BidiShape<
                HttpRequestMessage,
                ITransportOutbound,
                ITransportInbound,
                HttpResponseMessage>(
                connection.InRequest,
                connection.OutNetwork,
                connection.InNetwork,
                connection.OutResponse);
        }));
    }
}