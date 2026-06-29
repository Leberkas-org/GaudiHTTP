using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Server;

namespace GaudiHTTP.Streams.Lifecycle;

internal sealed class ListenerActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IMaterializer _materializer = Context.Materializer();
    private readonly IListenerFactory _factory;
    private readonly ListenerOptions _listenerOptions;
    private readonly GaudiServerOptions _serverOptions;
    private readonly IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> _bridgeGraph;
    private readonly IServerProtocolEngine _engine;
    private readonly string? _loggingCategory;

    public sealed record StartListening;
    public sealed record DrainConnections;

    internal sealed record ListeningStarted(int BoundPort, ListenerHandle Handle);

    private sealed record ConnectionArrived(Flow<ITransportOutbound, ITransportInbound, NotUsed> Connection);
    private sealed record ListenerCompleted;
    private sealed record ConnectionStopped;
    private sealed record BindFailed(Exception Error);

    private int _connectionIdCounter;
    private int _activeConnections;
    private bool _draining;
    private TaskCompletionSource<Done>? _completionTcs;

    public ListenerActor(
        IListenerFactory factory,
        ListenerOptions listenerOptions,
        GaudiServerOptions serverOptions,
        IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> bridgeGraph,
        IServerProtocolEngine engine,
        string? loggingCategory = null)
    {
        _factory = factory;
        _listenerOptions = listenerOptions;
        _serverOptions = serverOptions;
        _bridgeGraph = bridgeGraph;
        _engine = engine;
        _loggingCategory = loggingCategory;

        Receive<StartListening>(_ => OnStartListening());
        Receive<ConnectionArrived>(OnConnectionArrived);
        Receive<DrainConnections>(_ => OnDrainConnections());
        Receive<ConnectionStopped>(_ => OnConnectionStopped());
        Receive<ListenerCompleted>(_ => OnListenerCompleted());
        Receive<BindFailed>(OnBindFailed);
    }

    private void OnStartListening()
    {
        _log.Info("Listener starting on {0}:{1}", _listenerOptions.Host, _listenerOptions.Port);

        _completionTcs = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listenerSource = _factory.Bind(_listenerOptions);
        var self = Self;
        var sender = Sender;

        var (boundTask, acceptSwitch) = listenerSource
            .ViaMaterialized(
                KillSwitches.Single<Flow<ITransportOutbound, ITransportInbound, NotUsed>>(),
                Keep.Both)
            .To(Sink.ForEach<Flow<ITransportOutbound, ITransportInbound, NotUsed>>(
                connectionFlow => self.Tell(new ConnectionArrived(connectionFlow))))
            .Run(_materializer);

        var handle = new ListenerHandle(acceptSwitch, _completionTcs.Task);

        boundTask.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                sender.Tell(new ListeningStarted(t.Result, handle));
            }
            else
            {
                self.Tell(new BindFailed(t.Exception?.InnerException ?? t.Exception ?? new Exception("bind failed")));
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    private void OnBindFailed(BindFailed msg)
    {
        _log.Error(msg.Error, "Failed to bind on {0}:{1}", _listenerOptions.Host, _listenerOptions.Port);
        _completionTcs?.TrySetException(msg.Error);
        Context.Stop(Self);
    }

    private void OnConnectionArrived(ConnectionArrived msg)
    {
        var limit = _serverOptions.Limits.MaxConcurrentConnections;
        if (limit > 0 && _activeConnections >= limit)
        {
            RejectConnection(msg.Connection);
            return;
        }

        var connectionId = ++_connectionIdCounter;
        _activeConnections++;

        var child = Context.ActorOf(
            ConnectionActor.Props(connectionId, msg.Connection, _bridgeGraph, _engine, _serverOptions,
                loggingCategory: _loggingCategory),
            string.Concat("conn-", connectionId));

        Context.WatchWith(child, new ConnectionStopped());
    }

    private void OnDrainConnections()
    {
        _log.Info("Listener draining {0} active connection(s)", _activeConnections);
        _draining = true;

        foreach (var child in Context.GetChildren())
        {
            child.Tell(new ConnectionActor.Drain());
        }

        TryComplete();
    }

    private void OnConnectionStopped()
    {
        _activeConnections--;
        TryComplete();
    }

    private void OnListenerCompleted()
    {
        _log.Debug("Listener source completed");
        TryComplete();
    }

    private void TryComplete()
    {
        if (_draining && _activeConnections <= 0)
        {
            _completionTcs?.TrySetResult(Done.Instance);
        }
    }

    private void RejectConnection(Flow<ITransportOutbound, ITransportInbound, NotUsed> connectionFlow)
    {
        try
        {
            var killSwitch = KillSwitches.Shared(string.Concat("reject-", Guid.NewGuid()));

            Source.Empty<ITransportOutbound>()
                .Via(connectionFlow)
                .Via(killSwitch.Flow<ITransportInbound>())
                .RunWith(
                    Sink.Ignore<ITransportInbound>().MapMaterializedValue(_ => NotUsed.Instance),
                    _materializer);

            killSwitch.Shutdown();
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Error rejecting connection");
        }
    }

    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new OneForOneStrategy(ex =>
        {
            _log.Warning(ex, "ConnectionActor failed");
            return Directive.Stop;
        });
    }

    public static Props Create(
        IListenerFactory factory,
        ListenerOptions listenerOptions,
        GaudiServerOptions serverOptions,
        IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> bridgeGraph,
        IServerProtocolEngine engine,
        string? loggingCategory = null)
        => Props.Create(() => new ListenerActor(
            factory, listenerOptions, serverOptions, bridgeGraph, engine, loggingCategory));
}
