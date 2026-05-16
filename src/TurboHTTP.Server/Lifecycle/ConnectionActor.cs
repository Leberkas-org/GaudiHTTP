using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using TurboHTTP.Server.Middleware;
using TurboHTTP.Server.Routing;
using TurboHTTP.Server.Streams;
using TurboHTTP.Server.Streams.Stages;

namespace TurboHTTP.Server.Lifecycle;

internal sealed class ConnectionActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly string _connectionId;
    private SharedKillSwitch? _killSwitch;
    private bool _draining;
    private readonly CancellationTokenSource _cts = new();

    public sealed record Materialize(
        Flow<ITransportOutbound, ITransportInbound, NotUsed> ConnectionFlow,
        IServerProtocolEngine Engine,
        IReadOnlyList<IServerBidiStage> Middleware,
        RouteTable RouteTable,
        TurboConnectionInfo ConnectionInfo,
        IServiceProvider Services,
        IMaterializer Materializer);

    public sealed record GracefulStop(TimeSpan Timeout);
    public sealed record StreamCompleted(Exception? Error);
    public sealed record ConnectionCompleted(string ConnectionId, ConnectionCompletionReason Reason);

    public ConnectionActor(string connectionId)
    {
        _connectionId = connectionId;

        Receive<Materialize>(OnMaterialize);
        Receive<StreamCompleted>(OnStreamCompleted);
        Receive<GracefulStop>(OnGracefulStop);
        Receive<ReceiveTimeout>(_ => OnDrainTimeout());
    }

    private void OnMaterialize(Materialize msg)
    {
        _log.Debug("Connection {0} materializing pipeline", _connectionId);

        _killSwitch = KillSwitches.Shared("connection-" + _connectionId);

        var routingFlow = Flow.FromGraph(
            new RoutingStage(msg.RouteTable, msg.ConnectionInfo, msg.Services, _cts.Token));

        var httpPipeline = ServerMiddlewarePipelineBuilder.Build(routingFlow, msg.Middleware, msg.Services);

        var protocolBidi = msg.Engine.CreateFlow();
        var composed = protocolBidi.Join(httpPipeline);

        _ = msg.ConnectionFlow
            .Via(_killSwitch.Flow<ITransportInbound>())
            .Join(composed)
            .Run(msg.Materializer);
    }

    private void OnStreamCompleted(StreamCompleted msg)
    {
        var reason = _draining
            ? ConnectionCompletionReason.ServerShutdown
            : msg.Error is null
                ? ConnectionCompletionReason.Normal
                : ConnectionCompletionReason.Error;

        if (msg.Error is not null)
        {
            _log.Warning("Connection {0} stream failed: {1}", _connectionId, msg.Error.Message);
        }
        else
        {
            _log.Debug("Connection {0} stream completed normally", _connectionId);
        }

        var completion = new ConnectionCompleted(_connectionId, reason);
        Context.Parent.Tell(completion);
        // Defer the stop to ensure the message is delivered first
        Self.Tell(PoisonPill.Instance);
    }

    private void OnGracefulStop(GracefulStop msg)
    {
        _log.Info("Connection {0} graceful stop requested (timeout: {1})", _connectionId, msg.Timeout);
        _draining = true;
        _cts.Cancel();
        _killSwitch?.Shutdown();
        SetReceiveTimeout(msg.Timeout);
    }

    private void OnDrainTimeout()
    {
        _log.Warning("Connection {0} drain timeout expired", _connectionId);
        var completion = new ConnectionCompleted(_connectionId, ConnectionCompletionReason.Timeout);
        Context.Parent.Tell(completion);
        // Defer the stop to ensure the message is delivered first
        Self.Tell(PoisonPill.Instance);
    }

    public static Props Create(string connectionId)
        => Props.Create(() => new ConnectionActor(connectionId));
}
