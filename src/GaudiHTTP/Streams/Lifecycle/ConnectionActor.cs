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
    private readonly ILoggingAdapter? _connectionLog;
    private SharedKillSwitch? _drainSwitch;

    public static Props Props(
        int connectionId,
        Flow<ITransportOutbound, ITransportInbound, NotUsed> connectionFlow,
        IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> bridgeGraph,
        IServerProtocolEngine engine,
        GaudiServerOptions options,
        IServiceProvider? services = null,
        string? loggingCategory = null)
        => Akka.Actor.Props.Create(() => new ConnectionActor(
            connectionId, connectionFlow, bridgeGraph, engine, options, services, loggingCategory));

    public ConnectionActor(
        int connectionId,
        Flow<ITransportOutbound, ITransportInbound, NotUsed> connectionFlow,
        IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> bridgeGraph,
        IServerProtocolEngine engine,
        GaudiServerOptions options,
        IServiceProvider? services = null,
        string? loggingCategory = null)
    {
        // When a connection-logging category is configured (GaudiListenOptions.UseConnectionLogging),
        // emit connection lifecycle events under a logger whose source IS that category, so operators
        // can filter per-endpoint connection logs.
        _connectionLog = string.IsNullOrEmpty(loggingCategory)
            ? null
            : Logging.GetLogger(Context.System, loggingCategory);
        _connectionLog?.Info("Connection {0} accepted", connectionId);
        // Mirror the client's StreamOwner tuning: the default 16/16 input buffer throttles H2
        // multiplexing (more in-flight elements per materialized stream); H1.1 rarely fills it.
        var materializerSettings = ActorMaterializerSettings.Create(Context.System)
            .WithInputBuffer(initialSize: 32, maxSize: 128);
        var materializer = Context.Materializer(materializerSettings);
        _drainSwitch = KillSwitches.Shared(string.Concat("conn-", connectionId));

        var protocolBidi = engine.CreateFlow(services);
        var composed = protocolBidi.Join(Flow.FromGraph(bridgeGraph).Async());

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
            _connectionLog?.Info("Connection {0} closed", connectionId);
            Context.Stop(Self);
        });

        Receive<ConnectionFailed>(msg =>
        {
            _log.Warning(msg.Error, "Connection {0}: stream failed", connectionId);
            _connectionLog?.Info("Connection {0} closed with error: {1}", connectionId, msg.Error.Message);
            Context.Stop(Self);
        });
    }

    protected override void PostStop()
    {
        _drainSwitch = null;
    }
}
