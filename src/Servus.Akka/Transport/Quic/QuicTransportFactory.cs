using Akka;
using Akka.Actor;
using Akka.Streams.Dsl;

namespace Servus.Akka.Transport.Quic;

public sealed class QuicTransportFactory : ITransportFactory
{
    private readonly IActorRef _connectionManager;

    public QuicTransportFactory(IActorRef connectionManager)
    {
        _connectionManager = connectionManager;
    }

    public Flow<ITransportOutbound, ITransportInbound, NotUsed> Create()
    {
        return Flow.FromGraph(new QuicConnectionStage(_connectionManager));
    }
}