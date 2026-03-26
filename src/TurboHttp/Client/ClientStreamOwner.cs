using System;
using Akka.Actor;
using Akka.Event;
using OwnerMsg = TurboHttp.Client.ClientStreamOwner;
using InstanceMsg = TurboHttp.Client.ClientStreamInstance;

namespace TurboHttp.Client;

/// <summary>
/// Manages the lifecycle of a single <c>ClientStreamInstance</c> actor.
/// Tracks pending work from feature BidiStages, supervises the instance with
/// exponential-backoff retry, and coordinates graceful shutdown.
/// </summary>
internal sealed class ClientStreamOwnerActor : UntypedActor, IWithTimers
{
    private static readonly TimeSpan[] RetryBackoffs =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(2)
    ];

    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private const string RetryTimerKey = "retry-create";
    private const string ShutdownTimerKey = "shutdown-timeout";

    private readonly ILoggingAdapter _log = Context.GetLogger();

    // State
    private int _pending;
    private IActorRef _streamInstance = Nobody.Instance;
    private int _retryAttempts;
    private Exception? _lastError;

    // Tracks the original CreateStreamInstance message for retry recreation
    private OwnerMsg.CreateStreamInstance? _createRequest;

    // Tracks who requested creation so we can reply on success/failure
    private IActorRef _createRequester = Nobody.Instance;

    // Tracks whether a RequestStreamIdle is waiting for pending work to drain
    private OwnerMsg.RequestStreamIdle? _pendingIdleRequest;

    // Shutdown state
    private bool _shuttingDown;

    public ITimerScheduler Timers { get; set; } = null!;

    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new AllForOneStrategy(
            maxNrOfRetries: MaxRetryAttempts,
            withinTimeRange: TimeSpan.FromMinutes(1),
            localOnlyDecider: ex =>
            {
                _log.Warning("Stream instance supervised failure: {0}", ex.Message);
                return Directive.Stop;
            });
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case OwnerMsg.CreateStreamInstance create:
                HandleCreateStreamInstance(create);
                break;

            case OwnerMsg.PendingWorkSignal pending:
                HandlePendingWorkSignal(pending);
                break;

            case OwnerMsg.StreamInstanceFailed failed:
                HandleStreamInstanceFailed(failed);
                break;

            case OwnerMsg.RequestStreamIdle idle:
                HandleRequestStreamIdle(idle);
                break;

            case OwnerMsg.Shutdown:
                HandleShutdown();
                break;

            case InstanceMsg.StreamInitialized:
                HandleStreamInitialized();
                break;

            case InstanceMsg.StreamFailed streamFailed:
                HandleChildStreamFailed(streamFailed);
                break;

            case Terminated terminated:
                HandleTerminated(terminated);
                break;

            case RetryCreateInstance:
                ExecuteRetryCreate();
                break;

            case ShutdownTimeoutExpired:
                HandleShutdownTimeout();
                break;

            default:
                Unhandled(message);
                break;
        }
    }

    // ── CreateStreamInstance ──────────────────────────────────────────────

    private void HandleCreateStreamInstance(OwnerMsg.CreateStreamInstance create)
    {
        _log.Info("Creating stream instance (options: BaseAddress={0})",
            create.ClientOptions.BaseAddress);

        _createRequest = create;
        _createRequester = Sender;
        _retryAttempts = 0;
        _lastError = null;

        SpawnStreamInstance(create);
    }

    private void SpawnStreamInstance(OwnerMsg.CreateStreamInstance create)
    {
        var instanceProps = Props.Create(() => new ClientStreamInstanceActor())
            .WithSupervisorStrategy(SupervisorStrategy());

        _streamInstance = Context.ActorOf(instanceProps, $"stream-instance-{Guid.NewGuid():N}");
        Context.Watch(_streamInstance);

        _streamInstance.Tell(new InstanceMsg.InitializeStream(
            create.ClientOptions,
            create.RequestOptionsFactory,
            create.Pipeline,
            create.RequestReader,
            create.ResponseWriter));
    }

    // ── StreamInitialized (from child) ───────────────────────────────────

    private void HandleStreamInitialized()
    {
        _log.Info("Stream instance initialized successfully");
        _retryAttempts = 0;
        _lastError = null;

        if (!_createRequester.IsNobody())
        {
            _createRequester.Tell(new OwnerMsg.StreamInstanceCreated(_streamInstance));
        }
    }

    // ── StreamFailed (from child) ────────────────────────────────────────

    private void HandleChildStreamFailed(InstanceMsg.StreamFailed streamFailed)
    {
        _lastError = streamFailed.Reason;
        _retryAttempts++;

        _log.Warning("Stream instance failed (attempt {0}/{1}): {2}",
            _retryAttempts, MaxRetryAttempts, streamFailed.Reason.Message);

        // Stop the failed child before retry
        if (!_streamInstance.IsNobody())
        {
            Context.Unwatch(_streamInstance);
            Context.Stop(_streamInstance);
            _streamInstance = Nobody.Instance;
        }

        if (_retryAttempts <= MaxRetryAttempts && _createRequest is not null && !_shuttingDown)
        {
            var backoff = RetryBackoffs[Math.Min(_retryAttempts - 1, RetryBackoffs.Length - 1)];
            _log.Info("Scheduling retry attempt {0} after {1}ms backoff",
                _retryAttempts, backoff.TotalMilliseconds);

            Timers.StartSingleTimer(RetryTimerKey, RetryCreateInstance.Instance, backoff);
        }
        else
        {
            _log.Error("Stream instance creation failed after {0} attempts. Last error: {1}",
                _retryAttempts, _lastError?.Message);

            if (!_createRequester.IsNobody())
            {
                _createRequester.Tell(new OwnerMsg.StreamInstanceFailed(
                    _lastError!, _retryAttempts));
            }
        }
    }

    // ── StreamInstanceFailed (explicit message, e.g. from external) ──────

    private void HandleStreamInstanceFailed(OwnerMsg.StreamInstanceFailed failed)
    {
        _lastError = failed.Reason;
        _retryAttempts = failed.AttemptNumber;

        _log.Warning("Stream instance failure reported (attempt {0}): {1}",
            failed.AttemptNumber, failed.Reason.Message);

        if (!_streamInstance.IsNobody())
        {
            Context.Unwatch(_streamInstance);
            Context.Stop(_streamInstance);
            _streamInstance = Nobody.Instance;
        }

        if (_retryAttempts < MaxRetryAttempts && _createRequest is not null && !_shuttingDown)
        {
            var backoff = RetryBackoffs[Math.Min(_retryAttempts, RetryBackoffs.Length - 1)];
            _retryAttempts++;
            _log.Info("Scheduling retry attempt {0} after {1}ms backoff",
                _retryAttempts, backoff.TotalMilliseconds);

            Timers.StartSingleTimer(RetryTimerKey, RetryCreateInstance.Instance, backoff);
        }
        else
        {
            _log.Error("Retries exhausted ({0} attempts). Last error: {1}",
                _retryAttempts, _lastError?.Message);

            if (!_createRequester.IsNobody())
            {
                _createRequester.Tell(new OwnerMsg.StreamInstanceFailed(
                    _lastError!, _retryAttempts));
            }
        }
    }

    // ── Retry ────────────────────────────────────────────────────────────

    private void ExecuteRetryCreate()
    {
        if (_createRequest is null || _shuttingDown)
        {
            return;
        }

        _log.Info("Executing retry attempt {0}/{1}", _retryAttempts, MaxRetryAttempts);
        SpawnStreamInstance(_createRequest);
    }

    // ── PendingWorkSignal ────────────────────────────────────────────────

    private void HandlePendingWorkSignal(OwnerMsg.PendingWorkSignal signal)
    {
        var previous = _pending;
        _pending = Math.Max(0, _pending + signal.Delta);

        _log.Info("Pending work changed: {0} -> {1} (delta: {2})",
            previous, _pending, signal.Delta);

        // If pending work just reached zero and we have a deferred idle request, process it
        if (_pending == 0 && _pendingIdleRequest is not null)
        {
            _log.Info("Pending work drained, processing deferred idle request");
            ProcessIdleRequest(_pendingIdleRequest);
            _pendingIdleRequest = null;
        }

        // If pending work just reached zero and we're shutting down, proceed with shutdown
        if (_pending == 0 && _shuttingDown)
        {
            _log.Info("Pending work drained during shutdown, requesting instance shutdown");
            RequestInstanceShutdown();
        }
    }

    // ── RequestStreamIdle ────────────────────────────────────────────────

    private void HandleRequestStreamIdle(OwnerMsg.RequestStreamIdle idle)
    {
        if (_pending == 0)
        {
            _log.Info("Stream idle request granted (no pending work)");
            ProcessIdleRequest(idle);
        }
        else
        {
            _log.Info("Stream idle request deferred (pending work: {0})", _pending);
            _pendingIdleRequest = idle;
        }
    }

    private void ProcessIdleRequest(OwnerMsg.RequestStreamIdle idle)
    {
        if (!idle.RequestedBy.IsNobody())
        {
            idle.RequestedBy.Tell(new InstanceMsg.RequestShutdown());
        }
        else if (!_streamInstance.IsNobody())
        {
            _streamInstance.Tell(new InstanceMsg.RequestShutdown());
        }
    }

    // ── Shutdown ─────────────────────────────────────────────────────────

    private void HandleShutdown()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;

        _log.Info("Shutdown requested. Pending work: {0}", _pending);

        if (_pending == 0)
        {
            RequestInstanceShutdown();
        }
        else
        {
            _log.Info("Waiting up to {0}s for pending work to drain before shutdown",
                ShutdownTimeout.TotalSeconds);

            Timers.StartSingleTimer(ShutdownTimerKey, ShutdownTimeoutExpired.Instance, ShutdownTimeout);
        }
    }

    private void RequestInstanceShutdown()
    {
        Timers.Cancel(ShutdownTimerKey);

        if (!_streamInstance.IsNobody())
        {
            _log.Info("Requesting stream instance shutdown");
            _streamInstance.Tell(new InstanceMsg.RequestShutdown());
        }
        else
        {
            _log.Info("No stream instance to shut down, self-terminating");
            Context.Stop(Self);
        }
    }

    private void HandleShutdownTimeout()
    {
        _log.Warning("Shutdown timeout expired with {0} pending work items. Force-shutting down.",
            _pending);

        RequestInstanceShutdown();
    }

    // ── Terminated (child death watch) ───────────────────────────────────

    private void HandleTerminated(Terminated terminated)
    {
        if (terminated.ActorRef.Equals(_streamInstance))
        {
            _log.Info("Stream instance terminated");
            _streamInstance = Nobody.Instance;

            if (_shuttingDown)
            {
                _log.Info("Stream instance terminated during shutdown, self-terminating");
                Context.Stop(Self);
            }
        }
    }

    // ── Internal messages ────────────────────────────────────────────────

    /// <summary>Internal signal to trigger a retry of stream instance creation.</summary>
    private sealed class RetryCreateInstance
    {
        public static readonly RetryCreateInstance Instance = new();
        private RetryCreateInstance() { }
    }

    /// <summary>Internal signal that the shutdown timeout has expired.</summary>
    private sealed class ShutdownTimeoutExpired
    {
        public static readonly ShutdownTimeoutExpired Instance = new();
        private ShutdownTimeoutExpired() { }
    }
}

