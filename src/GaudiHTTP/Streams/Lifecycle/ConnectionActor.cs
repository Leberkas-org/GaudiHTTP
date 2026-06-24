using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Server;

namespace GaudiHTTP.Streams.Lifecycle;

internal sealed class ConnectionActor : ReceiveActor
{
    public sealed record Drain;
    private sealed record ConnectionCompleted;
    private sealed record ConnectionFailed(Exception Error);

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
        // Mirror the client's StreamOwner tuning: the default 16/16 input buffer throttles H2
        // multiplexing (more in-flight elements per materialized stream); H1.1 rarely fills it.
        var materializerSettings = ActorMaterializerSettings.Create(Context.System)
            .WithInputBuffer(initialSize: 32, maxSize: 128);
        var materializer = Context.Materializer(materializerSettings);
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
            .PipeTo(self,
                success: _ => new ConnectionCompleted(),
                failure: ex => new ConnectionFailed(ex));

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

        Receive<ConnectionFailed>(msg =>
        {
            _log.Warning(msg.Error, "Connection {0}: stream failed", connectionId);
            Context.Stop(Self);
        });
    }

    protected override void PostStop()
    {
        _drainSwitch = null;
    }
}
