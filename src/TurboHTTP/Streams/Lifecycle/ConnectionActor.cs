using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Server;

namespace TurboHTTP.Streams.Lifecycle;

internal sealed class ConnectionActor : ReceiveActor
{
    public sealed record Drain;
    private sealed record ConnectionCompleted;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private SharedKillSwitch? _drainSwitch;

    public static Props Props(
        int connectionId,
        Flow<ITransportOutbound, ITransportInbound, NotUsed> connectionFlow,
        IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> bridgeGraph,
        IServerProtocolEngine engine,
        TurboServerOptions options,
        IServiceProvider? services = null)
        => Akka.Actor.Props.Create(() => new ConnectionActor(
            connectionId, connectionFlow, bridgeGraph, engine, options, services));

    public ConnectionActor(
        int connectionId,
        Flow<ITransportOutbound, ITransportInbound, NotUsed> connectionFlow,
        IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> bridgeGraph,
        IServerProtocolEngine engine,
        TurboServerOptions options,
        IServiceProvider? services = null)
    {
        var materializer = Context.Materializer();
        _drainSwitch = KillSwitches.Shared(string.Concat("conn-", connectionId));

        var protocolBidi = engine.CreateFlow(services);
        var composed = protocolBidi.Join(Flow.FromGraph(bridgeGraph));

        var self = Self;
        connectionFlow
            .Via(_drainSwitch.Flow<ITransportInbound>())
            .ViaMaterialized(
                Flow.Create<ITransportInbound>().WatchTermination(Keep.Right),
                Keep.Right)
            .Join(composed)
            .Run(materializer)
            .PipeTo(self, success: _ => new ConnectionCompleted());

        Receive<Drain>(_ =>
        {
            _log.Debug("Connection {0}: draining", connectionId);
            _drainSwitch?.Shutdown();
        });

        Receive<ConnectionCompleted>(_ =>
        {
            _log.Debug("Connection {0}: completed", connectionId);
            Context.Stop(Self);
        });
    }
}
