using Akka;
using Akka.Actor;
using Akka.Streams.Dsl;
using Servus.Akka.IO.Tcp;

namespace Servus.Akka.IO.Quic;

/// <summary>
/// Transport factory for QUIC connections (HTTP/3).
/// Mirrors <see cref="TcpTransportFactory"/> — accepts a shared
/// <see cref="IActorRef"/> pointing to a <see cref="QuicConnectionManagerActor"/>.
/// </summary>
public sealed class QuicTransportFactory(
    IActorRef connectionManager,
    bool allowConnectionMigration = true)
{
    /// <summary>
    /// Creates a QUIC transport stage wired to the shared connection manager actor.
    /// </summary>
#pragma warning disable CA1416
    public Flow<IOutputItem, IInputItem, NotUsed> Create()
        => Flow.FromGraph(new QuicConnectionStage(connectionManager, allowConnectionMigration));
#pragma warning restore CA1416
}
