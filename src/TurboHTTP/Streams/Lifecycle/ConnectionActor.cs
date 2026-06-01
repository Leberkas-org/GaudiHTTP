using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

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
        ServerPipeline pipeline,
        IServerProtocolEngine engine,
        TurboServerOptions options,
        IServiceProvider? services = null)
        => Akka.Actor.Props.Create(() => new ConnectionActor(
            connectionId, connectionFlow, pipeline, engine, options, services));

    public ConnectionActor(
        int connectionId,
        Flow<ITransportOutbound, ITransportInbound, NotUsed> connectionFlow,
        ServerPipeline pipeline,
        IServerProtocolEngine engine,
        TurboServerOptions options,
        IServiceProvider? services = null)
    {
        var materializer = Context.Materializer();
        _drainSwitch = KillSwitches.Shared(string.Concat("conn-", connectionId));

        var protocolBidi = engine.CreateFlow(services);
        var isH2OrH3 = engine.ProtocolVersion.Major >= 2;
        var bridgeFlow = pipeline.CreateConnectionFlow(connectionId, unordered: isH2OrH3);
        var composed = protocolBidi.Join(bridgeFlow);

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
