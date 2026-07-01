using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Server;

namespace GaudiHTTP.Streams.Lifecycle;

internal sealed class ServerSupervisorActor : ReceiveActor, IWithTimers
{
    private const string StartupTimerKey = "startup-timeout";

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly List<ListenerHandle> _handles = [];
    private readonly List<IActorRef> _listenerActors = [];
    private readonly List<int> _boundPorts = [];
    private IActorRef _startRequester = ActorRefs.Nobody;
    private int _pendingListenerCount;
    private int _expectedListenerCount;
    private bool _startupComplete;
    private IActorRef _drainRequester = ActorRefs.Nobody;

    public sealed record StartServer(
        IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> BridgeFlow,
        GaudiServerOptions Options,
        IReadOnlyList<ListenerBinding> Bindings);

    public sealed record ListenersReady(IReadOnlyList<int> BoundPorts);
    public sealed record ListenersFailed(Exception Error);
    public sealed record StopAccepting;
    public sealed record BeginDrain(TimeSpan Timeout);
    public sealed record DrainComplete(bool TimedOut);

    private sealed record StartupTimedOut;
    private sealed record DrainTimedOut;
    private sealed record DrainSucceeded;

    public ITimerScheduler Timers { get; set; } = null!;

    public ServerSupervisorActor()
    {
        Receive<StartServer>(OnStartServer);
        Receive<ListenerActor.ListeningStarted>(OnListenerReady);
        Receive<Terminated>(OnListenerTerminated);
        Receive<StartupTimedOut>(_ => OnStartupTimedOut());
        Receive<StopAccepting>(_ => OnStopAccepting());
        Receive<BeginDrain>(OnBeginDrain);
        Receive<DrainSucceeded>(_ => OnDrainResult(timedOut: false));
        Receive<DrainTimedOut>(_ => OnDrainResult(timedOut: true));
    }

    private void OnStartServer(StartServer msg)
    {
        _startRequester = Sender;
        _pendingListenerCount = msg.Bindings.Count;
        _expectedListenerCount = msg.Bindings.Count;

        if (_pendingListenerCount == 0)
        {
            _startupComplete = true;
            _startRequester.Tell(new ListenersReady([]));
            _startRequester = ActorRefs.Nobody;
            return;
        }

        Timers.StartSingleTimer(StartupTimerKey, new StartupTimedOut(), msg.Options.StartupTimeout);

        for (var i = 0; i < msg.Bindings.Count; i++)
        {
            var binding = msg.Bindings[i];
            var engine = binding.Options is QuicListenerOptions
                ? ProtocolRouter.ResolveEngine(new Version(3, 0), msg.Options)
                : ProtocolRouter.ResolveNegotiating(msg.Options, binding.Protocols);

            var props = ListenerActor.Create(
                binding.Factory,
                binding.Options,
                msg.Options,
                msg.BridgeFlow,
                engine,
                binding.ConnectionLoggingCategory);

            var name = string.Concat("listener-", i);
            var listener = Context.ActorOf(props, name);
            Context.Watch(listener);
            _listenerActors.Add(listener);
            listener.Tell(new ListenerActor.StartListening());
        }
    }

    private void OnListenerReady(ListenerActor.ListeningStarted msg)
    {
        if (_startupComplete)
        {
            return;
        }

        _boundPorts.Add(msg.BoundPort);
        _handles.Add(msg.Handle);
        _pendingListenerCount--;

        if (_pendingListenerCount <= 0)
        {
            _startupComplete = true;
            Timers.Cancel(StartupTimerKey);
            _log.Info("All {0} listener(s) ready", _handles.Count);
            _startRequester.Tell(new ListenersReady(_boundPorts));
            _startRequester = ActorRefs.Nobody;
        }
    }

    private void OnListenerTerminated(Terminated msg)
    {
        if (!_startupComplete)
        {
            _startupComplete = true;
            Timers.Cancel(StartupTimerKey);
            _log.Error("Listener {0} died during startup", msg.ActorRef.Path.Name);

            foreach (var listener in _listenerActors)
            {
                if (!listener.Equals(msg.ActorRef))
                {
                    Context.Stop(listener);
                }
            }

            _startRequester.Tell(new ListenersFailed(
                new InvalidOperationException(
                    string.Concat("Listener ", msg.ActorRef.Path.Name, " failed during startup"))));
            _startRequester = ActorRefs.Nobody;
            return;
        }

        _log.Warning("Listener {0} terminated after startup", msg.ActorRef.Path.Name);
    }

    private void OnStartupTimedOut()
    {
        if (_startupComplete)
        {
            return;
        }

        _startupComplete = true;
        _log.Error("Listener startup timed out — {0}/{1} listeners ready",
            _expectedListenerCount - _pendingListenerCount, _expectedListenerCount);

        foreach (var listener in _listenerActors)
        {
            Context.Stop(listener);
        }

        _startRequester.Tell(new ListenersFailed(
            new TimeoutException(string.Concat(
                "Only ", (_expectedListenerCount - _pendingListenerCount).ToString(),
                "/", _expectedListenerCount.ToString(), " listeners started within timeout"))));
        _startRequester = ActorRefs.Nobody;
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

        if (_handles.Count == 0)
        {
            _drainRequester.Tell(new DrainComplete(TimedOut: false));
            _drainRequester = ActorRefs.Nobody;
            return;
        }

        var self = Self;
        var completionTasks = new List<Task>(_handles.Count);

        foreach (var listenerActor in _listenerActors)
        {
            listenerActor.Tell(new ListenerActor.DrainConnections());
        }

        foreach (var handle in _handles)
        {
            completionTasks.Add(handle.CompletionTask);
        }

        var drainAll = Task.WhenAll(completionTasks);
        var timeout = Task.Delay(msg.Timeout);

        Task.WhenAny(drainAll, timeout).ContinueWith(_ =>
        {
            if (drainAll.IsCompleted)
            {
                return (object)new DrainSucceeded();
            }

            return new DrainTimedOut();
        }, TaskContinuationOptions.ExecuteSynchronously).PipeTo(self);
    }

    private void OnDrainResult(bool timedOut)
    {
        if (timedOut)
        {
            _log.Warning("Supervisor: drain timed out");
        }
        else
        {
            _log.Info("Supervisor: drain completed cleanly");
        }

        _drainRequester.Tell(new DrainComplete(timedOut));
        _drainRequester = ActorRefs.Nobody;
    }

    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new AllForOneStrategy(
            maxNrOfRetries: 0,
            withinTimeRange: TimeSpan.FromMinutes(1),
            localOnlyDecider: ex =>
            {
                _log.Error(ex, "Listener failed");
                return Directive.Stop;
            });
    }
}
