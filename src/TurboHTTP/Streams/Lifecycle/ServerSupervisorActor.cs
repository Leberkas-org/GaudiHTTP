using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams.Lifecycle;

internal sealed class ServerSupervisorActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly List<ConnectionStageHandle> _handles = [];
    private readonly List<int> _boundPorts = [];
    private IActorRef _startRequester = ActorRefs.Nobody;
    private int _pendingListenerCount;
    private IActorRef _drainRequester = ActorRefs.Nobody;
    private SharedKillSwitch? _pipelineKillSwitch;

    public sealed record StartServer(
        IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> BridgeFlow,
        TurboServerOptions Options,
        IReadOnlyList<ListenerBinding> Bindings);

    public sealed record ListenersReady(IReadOnlyList<int> BoundPorts);
    public sealed record StopAccepting;
    public sealed record BeginDrain(TimeSpan Timeout);
    public sealed record DrainComplete;

    public ServerSupervisorActor()
    {
        Receive<StartServer>(OnStartServer);
        Receive<ListenerActor.ListeningStarted>(OnListenerReady);
        Receive<StopAccepting>(_ => OnStopAccepting());
        Receive<BeginDrain>(OnBeginDrain);
        Receive<DrainComplete>(OnDrainComplete);
    }

    private void OnStartServer(StartServer msg)
    {
        _startRequester = Sender;
        var materializer = Context.Materializer();

        _pipelineKillSwitch = KillSwitches.Shared("server-pipeline");

        var responseHub = new ResponseDispatcherHub();

        var (requestSink, responseDispatcher) = MergeHub.Source<IFeatureCollection>(perProducerBufferSize: 64)
            .Via(_pipelineKillSwitch.Flow<IFeatureCollection>())
            .Via(msg.BridgeFlow)
            .ToMaterialized(responseHub, Keep.Both)
            .Run(materializer);

        var dispatcher = new FairShareDispatcher(
            msg.Options.Limits.MaxConcurrentRequests,
            msg.Options.Limits.MinRequestGuarantee);

        var pipelineHandles = new PipelineHandles(requestSink, responseDispatcher, dispatcher);

        _pendingListenerCount = msg.Bindings.Count;

        if (_pendingListenerCount == 0)
        {
            _startRequester.Tell(new ListenersReady([]));
            return;
        }

        for (var i = 0; i < msg.Bindings.Count; i++)
        {
            var binding = msg.Bindings[i];
            var engine = binding.Options is QuicListenerOptions
                ? ProtocolRouter.ResolveEngine(new Version(3, 0), msg.Options)
                : ProtocolRouter.ResolveNegotiating(msg.Options);

            var props = ListenerActor.Create(
                binding.Factory,
                binding.Options,
                msg.Options,
                pipelineHandles,
                engine);

            var name = string.Concat("listener-", i);
            var listener = Context.ActorOf(props, name);
            listener.Tell(new ListenerActor.StartListening());
        }
    }

    private void OnListenerReady(ListenerActor.ListeningStarted msg)
    {
        _boundPorts.Add(msg.BoundPort);
        _handles.Add(msg.Handle);
        _pendingListenerCount--;

        if (_pendingListenerCount <= 0)
        {
            _log.Info("All {0} listener(s) ready", _handles.Count);
            _startRequester.Tell(new ListenersReady(_boundPorts));
            _startRequester = ActorRefs.Nobody;
        }
    }

    private void OnStopAccepting()
    {
        _log.Info("Supervisor: stop accepting on all listeners");
        foreach (var handle in _handles)
        {
            handle.AcceptSwitch.Shutdown();
        }
    }

    private void OnBeginDrain(BeginDrain msg)
    {
        _log.Info("Supervisor: initiating graceful drain (timeout: {0})", msg.Timeout);
        _drainRequester = Sender;

        _pipelineKillSwitch?.Shutdown();

        if (_handles.Count == 0)
        {
            Sender.Tell(new DrainComplete());
            _drainRequester = ActorRefs.Nobody;
            return;
        }

        var self = Self;
        var completionTasks = new List<Task>(_handles.Count);

        foreach (var handle in _handles)
        {
            handle.DrainSwitch.Shutdown();
            completionTasks.Add(handle.CompletionTask);
        }

        Task.WhenAny(
            Task.WhenAll(completionTasks),
            Task.Delay(msg.Timeout))
            .PipeTo(self,
                success: _ => new DrainComplete(),
                failure: ex => new DrainComplete());
    }

    private void OnDrainComplete(DrainComplete msg)
    {
        _log.Info("Supervisor: drain completed");
        _drainRequester.Tell(new DrainComplete());
        _drainRequester = ActorRefs.Nobody;
    }

    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new OneForOneStrategy(
            maxNrOfRetries: 3,
            withinTimeRange: TimeSpan.FromMinutes(1),
            localOnlyDecider: ex => ex switch
            {
                _ => Directive.Restart
            });
    }
}
