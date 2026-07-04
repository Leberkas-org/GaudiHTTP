using System.Diagnostics;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Diagnostics;
using GaudiHTTP.Server.Context.Features;
using static Servus.Senf;

namespace GaudiHTTP.Streams.Stages.Server;

internal sealed class ApplicationBridgeStage<TContext> : GraphStage<FlowShape<IFeatureCollection, IFeatureCollection>>
    where TContext : notnull
{
    private readonly IHttpApplication<TContext> _application;
    private readonly int _parallelism;
    private readonly TimeSpan _handlerTimeout;
    private readonly TimeSpan _handlerGracePeriod;

    private readonly Inlet<IFeatureCollection> _in = new("AppBridge.In");
    private readonly Outlet<IFeatureCollection> _out = new("AppBridge.Out");

    public override FlowShape<IFeatureCollection, IFeatureCollection> Shape { get; }

    public ApplicationBridgeStage(
        IHttpApplication<TContext> application,
        int parallelism,
        TimeSpan handlerTimeout,
        TimeSpan handlerGracePeriod)
    {
        _application = application;
        _parallelism = parallelism;
        _handlerTimeout = handlerTimeout;
        _handlerGracePeriod = handlerGracePeriod;
        Shape = new FlowShape<IFeatureCollection, IFeatureCollection>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private readonly record struct DispatchCompleted(int Sequence, IFeatureCollection Features);

    private readonly record struct DispatchFailed(int Sequence, IFeatureCollection Features, Exception Error);

    private readonly record struct ResponseReady(int Sequence, IFeatureCollection Features, Task HandlerTask);

    private readonly record struct HandlerFinished(int Sequence, IFeatureCollection Features);

    private readonly record struct HandlerFaulted(int Sequence, IFeatureCollection Features, Exception Error);

    private sealed class Logic : TimerGraphStageLogic
    {
        private const string SoftTimerPrefix = "soft:";
        private const string HardTimerPrefix = "hard:";

        private sealed class RequestSlot
        {
            public required IFeatureCollection Features { get; init; }
            public TContext? AppContext { get; set; }
            public CancellationTokenSource? Cts { get; set; }
            public string? SoftTimerKey { get; set; }
            public string? HardTimerKey { get; set; }
            public bool InGracePhase { get; set; }
        }

        private readonly ApplicationBridgeStage<TContext> _stage;
        private IActorRef? _stageActor;
        private bool _upstreamFinished;
        private int _inFlight;
        private int _sequence;
        private bool _downstreamReady;
        private readonly Queue<IFeatureCollection> _pending = new();
        private readonly Dictionary<int, RequestSlot> _requestSlots = [];
        private readonly bool _metricsEnabled;
        private readonly int _backpressureThreshold;
        private bool _backpressureSignaled;

        // Actor-confined pool — no cross-thread access, Interlocked ops in ObjectPool are harmless.
        private readonly ObjectPool<CancellationTokenSource> _ctsPool = new(16);

        public Logic(ApplicationBridgeStage<TContext> stage) : base(stage.Shape)
        {
            _stage = stage;
            _metricsEnabled = Metrics.PipelineInFlight().Enabled
                              || Metrics.PipelinePending().Enabled
                              || Metrics.HandlerTimeouts().Enabled
                              || Tracing.IsServerTracingActive();
            _backpressureThreshold = (int)(stage._parallelism * 0.8);

            SetHandler(stage._in,
                onPush: OnPush,
                onUpstreamFinish: () =>
                {
                    Tracing.For("Handler").Info(this, "bridge upstream finished (protocol completed), inFlight={0}", _inFlight);
                    _upstreamFinished = true;
                    TryCompleteIfDrained();
                },
                onUpstreamFailure: ex =>
                {
                    Tracing.For("Handler").Warning(this, "bridge upstream failure: {0}, inFlight={1}", ex.Message, _inFlight);
                    CancelAllInFlight();
                    FailStage(ex);
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    _downstreamReady = true;
                    TryEmitPending();
                    TryPullNext();
                },
                onDownstreamFinish: cause =>
                {
                    Tracing.For("Handler").Info(this, "bridge downstream finished: {0}, inFlight={1}",
                        cause?.Message ?? "normal", _inFlight);
                    CancelAllInFlight();
                    if (!IsClosed(stage._in))
                    {
                        Cancel(stage._in);
                    }
                });
        }

        public override void PreStart()
        {
            Tracing.For("Handler").Debug(this, "bridge PreStart");
            _stageActor = GetStageActor(OnMessage).Ref;
            Pull(_stage._in);
        }

        protected override void OnTimer(object timerKey)
        {
            if (timerKey is not string key)
            {
                return;
            }

            if (key.StartsWith(SoftTimerPrefix) && int.TryParse(key.AsSpan(SoftTimerPrefix.Length), out var softSeq))
            {
                OnSoftTimeout(softSeq);
            }
            else if (key.StartsWith(HardTimerPrefix) && int.TryParse(key.AsSpan(HardTimerPrefix.Length), out var hardSeq))
            {
                OnHardTimeout(hardSeq);
            }
        }

        private void OnSoftTimeout(int seq)
        {
            if (!_requestSlots.TryGetValue(seq, out var slot))
            {
                return;
            }

            slot.Cts?.Cancel();
            slot.InGracePhase = true;
            if (slot.HardTimerKey is not null)
            {
                ScheduleOnce(slot.HardTimerKey, _stage._handlerGracePeriod);
            }
        }

        private void OnHardTimeout(int seq)
        {
            if (!_requestSlots.TryGetValue(seq, out var slot) || !slot.InGracePhase)
            {
                return;
            }

            var features = slot.Features;

            // Only emit when the response has not already gone out. A streaming handler whose headers
            // were emitted (ResponseReady's still-running branch) already pushed these features once;
            // re-emitting here would deliver the same response twice (double OnResponse / wire
            // corruption / double in-flight accounting). Completing the body (inside FinishRequest)
            // ends the stalled stream; the late HandlerFinished is swallowed because the timeout entry
            // is gone.
            var alreadyStarted = features.Get<IHttpResponseBodyFeature>() is GaudiHttpResponseBodyFeature
            {
                HasStarted: true
            };

            FinishRequest(seq, features, error: null, failStatus: alreadyStarted ? null : 503,
                emit: !alreadyStarted, timedOut: true);

            TryCompleteIfDrained();
        }

        private void OnPush()
        {
            var features = Grab(_stage._in);
            var seq = _sequence++;

            _inFlight++;
            if (_metricsEnabled)
            {
                Metrics.PipelineInFlight().Add(1);
                CheckBackpressure();
            }

            try
            {
                DispatchAsync(features, seq);
            }
            catch (Exception)
            {
                FinishRequest(seq, features, error: null, failStatus: 500, emit: true,
                    cleanupSlot: false, resetBackpressure: false);
            }

            TryPullNext();
        }

        private void DispatchAsync(IFeatureCollection features, int seq)
        {
            TContext appContext;
            try
            {
                appContext = _stage._application.CreateContext(ContainerFor(features));
            }
            catch (Exception)
            {
                FinishRequest(seq, features, error: null, failStatus: 500, emit: true,
                    cleanupSlot: false, trackMetrics: false);
                return;
            }

            var slot = new RequestSlot { Features = features, AppContext = appContext };
            _requestSlots[seq] = slot;

            var task = _stage._application.ProcessRequestAsync(appContext);

            if (task.IsCompletedSuccessfully)
            {
                FinishRequest(seq, features, error: null, failStatus: null, emit: true, trackMetrics: false);
            }
            else if (task.IsFaulted)
            {
                FinishRequest(seq, features, error: task.Exception, failStatus: 500, emit: true, trackMetrics: false);
            }
            else
            {
                if (!_ctsPool.TryRent(out var cts))
                {
                    cts = new CancellationTokenSource();
                }
                var softKey = string.Create(SoftTimerPrefix.Length + 10, seq, static (span, s) =>
                {
                    SoftTimerPrefix.AsSpan().CopyTo(span);
                    s.TryFormat(span[SoftTimerPrefix.Length..], out _);
                });
                var hardKey = string.Create(HardTimerPrefix.Length + 10, seq, static (span, s) =>
                {
                    HardTimerPrefix.AsSpan().CopyTo(span);
                    s.TryFormat(span[HardTimerPrefix.Length..], out _);
                });
                slot.Cts = cts;
                slot.SoftTimerKey = softKey;
                slot.HardTimerKey = hardKey;
                ScheduleOnce(softKey, _stage._handlerTimeout);

                var bodyFeature = features.Get<IHttpResponseBodyFeature>() as GaudiHttpResponseBodyFeature;
                var headersReady = bodyFeature?.WhenHeadersReady;

                if (headersReady is not null)
                {
                    Task.WhenAny(headersReady, task)
                        .PipeTo(_stageActor!,
                            success: () => new ResponseReady(seq, features, task));
                }
                else
                {
                    task.PipeTo(_stageActor!,
                        success: () => new DispatchCompleted(seq, features),
                        failure: ex => new DispatchFailed(seq, features, ex));
                }
            }
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            switch (args.msg)
            {
                case ResponseReady(var seq, var features, var handlerTask):
                    if (handlerTask.IsCompleted)
                    {
                        var notStarted = features.Get<IHttpResponseBodyFeature>() is not GaudiHttpResponseBodyFeature
                        {
                            HasStarted: true
                        };

                        FinishRequest(seq, features, error: handlerTask.Exception,
                            failStatus: handlerTask.IsFaulted && notStarted ? 500 : null, emit: true);
                    }
                    else
                    {
                        Emit(features);
                        handlerTask.PipeTo(_stageActor!,
                            success: () => new HandlerFinished(seq, features),
                            failure: ex => new HandlerFaulted(seq, features, ex));
                    }

                    break;

                case HandlerFinished(var seq, var finishedFeatures):
                    if (!_requestSlots.ContainsKey(seq))
                    {
                        break;
                    }

                    FinishRequest(seq, finishedFeatures, error: null, failStatus: null, emit: false);
                    TryCompleteIfDrained();
                    break;

                case HandlerFaulted(var seq, var faultedFeatures, var error):
                    if (!_requestSlots.ContainsKey(seq))
                    {
                        break;
                    }

                    FinishRequest(seq, faultedFeatures, error: error, failStatus: null, emit: false);
                    TryCompleteIfDrained();
                    break;

                case DispatchCompleted(var seq, var features):
                    if (!_requestSlots.ContainsKey(seq))
                    {
                        break;
                    }

                    FinishRequest(seq, features, error: null, failStatus: null, emit: true);
                    break;

                case DispatchFailed(var seq, var features, var error):
                    if (!_requestSlots.ContainsKey(seq))
                    {
                        break;
                    }

                    FinishRequest(seq, features, error: error, failStatus: 500, emit: true);
                    break;
            }

            if (_upstreamFinished && _inFlight == 0 && _pending.Count == 0)
            {
                CompleteStage();
            }
        }

        // Owns the complete per-request finish sequence: optional fail-status stamping, response-body
        // completion, OnCompleted firing, in-flight/metrics bookkeeping, request-slot teardown
        // (timers + CTS + app context disposal), and the optional emit. Every finish path routes
        // through here so a request's bookkeeping is torn down exactly once, in exactly one place.
        // The trailing bool knobs preserve pre-existing per-callsite asymmetries (OnPush's dispatch
        // catch never tracked ResetBackpressure; DispatchAsync's synchronous branches never touched
        // the in-flight metric at all) rather than silently changing behavior during the merge.
        private void FinishRequest(
            int seq,
            IFeatureCollection features,
            Exception? error,
            int? failStatus,
            bool emit,
            bool cleanupSlot = true,
            bool trackMetrics = true,
            bool resetBackpressure = true,
            bool timedOut = false)
        {
            if (failStatus is int status)
            {
                var responseFeature = features.Get<IHttpResponseFeature>();
                responseFeature?.StatusCode = status;
            }

            CompleteResponseBody(features);
            FireOnCompleted(features);
            _inFlight--;
            if (trackMetrics && _metricsEnabled)
            {
                if (timedOut)
                {
                    Metrics.HandlerTimeouts().Add(1);
                }

                Metrics.PipelineInFlight().Add(-1);
                if (resetBackpressure)
                {
                    ResetBackpressure();
                }
            }

            if (cleanupSlot && _requestSlots.Remove(seq, out var slot))
            {
                CleanupTimeout(slot);
                DisposeAppContext(slot, error);
            }

            if (emit)
            {
                Emit(features);
            }
        }

        private void TryCompleteIfDrained()
        {
            if (_upstreamFinished && _inFlight == 0)
            {
                CompleteStage();
            }
        }

        // ASP.NET's HostingApplication.CreateContext reuses its cached host context (and the
        // DefaultHttpContext graph) only when the collection it is handed implements
        // IHostContextContainer<TContext>. The pooled GaudiFeatureCollection is non-generic, so we wrap
        // it in a per-collection HostContextContainer<TContext> (cached on the collection) and hand the
        // wrapper to CreateContext. Non-Gaudi collections (tests, custom transports) fall back to no reuse.
        private IFeatureCollection ContainerFor(IFeatureCollection features)
        {
            if (features is not GaudiFeatureCollection gaudi)
            {
                return features;
            }

            if (gaudi.HostContextWrapper is HostContextContainer<TContext> existing)
            {
                return existing;
            }

            var container = new HostContextContainer<TContext>(gaudi);
            gaudi.HostContextWrapper = container;
            return container;
        }

        private void DisposeAppContext(RequestSlot slot, Exception? exception)
        {
            if (slot.AppContext is { } appContext)
            {
                _stage._application.DisposeContext(appContext, exception);
            }
        }

        private void CancelAllInFlight()
        {
            foreach (var slot in _requestSlots.Values)
            {
                slot.Cts?.Cancel();
            }
        }

        private void CleanupTimeout(RequestSlot slot)
        {
            if (slot.SoftTimerKey is not null)
            {
                CancelTimer(slot.SoftTimerKey);
            }

            if (slot.HardTimerKey is not null)
            {
                CancelTimer(slot.HardTimerKey);
            }

            if (slot.Cts is { } cts && (!cts.TryReset() || !_ctsPool.TryReturn(cts)))
            {
                cts.Dispose();
            }
        }

        private void TryPullNext()
        {
            if (_inFlight < _stage._parallelism && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        private void Emit(IFeatureCollection features)
        {
            if (_downstreamReady)
            {
                _downstreamReady = false;
                Push(_stage._out, features);
            }
            else
            {
                _pending.Enqueue(features);
                if (_metricsEnabled)
                {
                    Metrics.PipelinePending().Add(1);
                }
            }
        }

        private void TryEmitPending()
        {
            if (_downstreamReady && _pending.Count > 0)
            {
                _downstreamReady = false;
                Push(_stage._out, _pending.Dequeue());
                if (_metricsEnabled)
                {
                    Metrics.PipelinePending().Add(-1);
                }
            }
        }

        private static void CompleteResponseBody(IFeatureCollection features)
        {
            var bodyFeature = features.Get<IHttpResponseBodyFeature>() as GaudiHttpResponseBodyFeature;
            bodyFeature?.Complete();
        }

        private static void FireOnCompleted(IFeatureCollection features)
        {
            if (features.Get<IHttpResponseFeature>() is GaudiHttpResponseFeature { HasOnCompletedCallbacks: true } responseFeature)
            {
                responseFeature.FireOnCompletedAsync().ContinueWith(static _ => { }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void CheckBackpressure()
        {
            if (_inFlight >= _backpressureThreshold && !_backpressureSignaled)
            {
                _backpressureSignaled = true;
                if (Activity.Current is { } connectionActivity)
                {
                    Tracing.AddBackpressureEvent(connectionActivity, _inFlight, _stage._parallelism);
                }
            }
        }

        private void ResetBackpressure()
        {
            if (_backpressureSignaled && _inFlight < _backpressureThreshold)
            {
                _backpressureSignaled = false;
            }
        }

        public override void PostStop()
        {
            Tracing.For("Handler").Info(this, "bridge PostStop (upstreamFinished={0}, inFlight={1})",
                _upstreamFinished, _inFlight);

            foreach (var slot in _requestSlots.Values)
            {
                if (slot.Cts is not null)
                {
                    if (slot.Features.Get<IHttpRequestLifetimeFeature>() is GaudiHttpRequestLifetimeFeature lifetime)
                    {
                        lifetime.Abort();
                    }

                    CompleteResponseBody(slot.Features);

                    slot.Cts.Cancel();
                    slot.Cts.Dispose();
                }

                if (slot.AppContext is { } appContext)
                {
                    _stage._application.DisposeContext(appContext, null);
                }
            }

            while (_ctsPool.TryRent(out var pooledCts))
            {
                pooledCts.Dispose();
            }

            _requestSlots.Clear();
        }
    }
}
