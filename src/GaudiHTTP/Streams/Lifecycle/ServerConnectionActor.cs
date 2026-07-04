using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Server;
using static Servus.Senf;

namespace GaudiHTTP.Streams.Lifecycle;

internal sealed class ServerConnectionActor : ReceiveActor
{
    private const string TraceCategory = "Lifecycle";

    public sealed record Drain;
    private sealed record ConnectionCompleted;
    private sealed record ConnectionFailed(Exception Error);

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly ILoggingAdapter? _connectionLog;
    private readonly int _connectionId;
    private SharedKillSwitch? _drainSwitch;

    public static Props Props(
        int connectionId,
        Flow<ITransportOutbound, ITransportInbound, NotUsed> connectionFlow,
        IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> bridgeGraph,
        IServerProtocolEngine engine,
        GaudiServerOptions options,
        IServiceProvider? services = null,
        string? loggingCategory = null)
        => Akka.Actor.Props.Create(() => new ServerConnectionActor(
            connectionId, connectionFlow, bridgeGraph, engine, options, services, loggingCategory));

    public ServerConnectionActor(
        int connectionId,
        Flow<ITransportOutbound, ITransportInbound, NotUsed> connectionFlow,
        IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> bridgeGraph,
        IServerProtocolEngine engine,
        GaudiServerOptions options,
        IServiceProvider? services = null,
        string? loggingCategory = null)
    {
        _connectionId = connectionId;
        _connectionLog = string.IsNullOrEmpty(loggingCategory)
            ? null
            : Logging.GetLogger(Context.System, loggingCategory);

        Tracing.For(TraceCategory).Info(this, "connection {0} accepted, engine={1}",
            connectionId, engine.GetType().Name);

        _connectionLog?.Info("Connection {0} accepted", connectionId);

        var materializerSettings = ActorMaterializerSettings.Create(Context.System)
            .WithInputBuffer(initialSize: 32, maxSize: 128);
        var materializer = Context.Materializer(materializerSettings);
        _drainSwitch = KillSwitches.Shared(string.Concat("conn-", connectionId));

        var protocolBidi = engine.CreateFlow(services);
        var composed = protocolBidi.Join(Flow.FromGraph(bridgeGraph).Async());

        Tracing.For(TraceCategory).Debug(this, "connection {0} materializing stream pipeline", connectionId);

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

        Tracing.For(TraceCategory).Debug(this, "connection {0} stream materialized", connectionId);

        Receive<Drain>(_ =>
        {
            Tracing.For(TraceCategory).Debug(this, "connection {0} draining", connectionId);
            _drainSwitch?.Shutdown();
        });

        Receive<ConnectionCompleted>(_ =>
        {
            Tracing.For(TraceCategory).Info(this, "connection {0} completed", connectionId);
            _connectionLog?.Info("Connection {0} closed", connectionId);
            Context.Stop(Self);
        });

        Receive<ConnectionFailed>(msg =>
        {
            Tracing.For(TraceCategory).Warning(this, "connection {0} stream failed: {1}",
                connectionId, msg.Error.Message);
            _log.Warning(msg.Error, "Connection {0}: stream failed", connectionId);
            _connectionLog?.Info("Connection {0} closed with error: {1}", connectionId, msg.Error.Message);
            Context.Stop(Self);
        });
    }

    protected override void PreRestart(Exception reason, object message)
    {
        Tracing.For(TraceCategory).Error(this, "connection {0} restarting: {1}",
            _connectionId, reason.Message);
        base.PreRestart(reason, message);
    }

    protected override void PostStop()
    {
        Tracing.For(TraceCategory).Debug(this, "connection {0} stopped", _connectionId);
        _drainSwitch = null;
    }
}
