using System.Diagnostics;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Diagnostics;
using TurboHTTP.Server.Context.Features;
using static Servus.Senf;

namespace TurboHTTP.Streams.Stages.Server;

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

        private readonly ApplicationBridgeStage<TContext> _stage;
        private IActorRef? _stageActor;
        private bool _upstreamFinished;
        private int _inFlight;
        private int _sequence;
        private bool _downstreamReady;
        private readonly Queue<IFeatureCollection> _pending = new();
        private readonly Dictionary<int, CancellationTokenSource> _activeTimeouts = [];
        private readonly Dictionary<int, IFeatureCollection> _activeFeatures = [];
        private readonly HashSet<int> _gracePhase = [];
        private readonly Dictionary<int, TContext> _appContexts = [];
        private readonly Dictionary<int, (string Soft, string Hard)> _timerKeys = [];
        private readonly bool _metricsEnabled;
        private readonly int _backpressureThreshold;
        private bool _backpressureSignaled;

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
                    _upstreamFinished = true;
                    if (_inFlight == 0)
                    {
                        CompleteStage();
                    }
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    _downstreamReady = true;
                    TryEmitPending();
                    TryPullNext();
                });
        }

        public override void PreStart()
        {
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
            if (!_activeTimeouts.TryGetValue(seq, out var cts))
            {
                return;
            }

            cts.Cancel();
            _gracePhase.Add(seq);
            if (_timerKeys.TryGetValue(seq, out var keys))
            {
                ScheduleOnce(keys.Hard, _stage._handlerGracePeriod);
            }
        }

        private void OnHardTimeout(int seq)
        {
            if (!_activeTimeouts.ContainsKey(seq) || !_gracePhase.Contains(seq))
            {
                return;
            }

            if (!_activeFeatures.TryGetValue(seq, out var features))
            {
                return;
            }

            CleanupTimeout(seq);
            _inFlight--;
            if (_metricsEnabled)
            {
                Metrics.HandlerTimeouts().Add(1);
                Metrics.PipelineInFlight().Add(-1);
                ResetBackpressure();
            }

            DisposeAppContext(seq, null);

            var alreadyStarted = features.Get<IHttpResponseBodyFeature>() is TurboHttpResponseBodyFeature
            {
                HasStarted: true
            };

            if (!alreadyStarted)
            {
                var responseFeature = features.Get<IHttpResponseFeature>();
                responseFeature?.StatusCode = 503;
            }

            CompleteResponseBody(features);
            FireOnCompleted(features);

            // Only emit when the response has not already gone out. A streaming handler whose headers
            // were emitted (ResponseReady's still-running branch) already pushed these features once;
            // re-emitting here would deliver the same response twice (double OnResponse / wire
            // corruption / double in-flight accounting). Completing the body above ends the stalled
            // stream; the late HandlerFinished is swallowed because the timeout entry is gone.
            if (!alreadyStarted)
            {
                Emit(features);
            }

            if (_upstreamFinished && _inFlight == 0)
            {
                CompleteStage();
            }
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
                _inFlight--;
                if (_metricsEnabled)
                {
                    Metrics.PipelineInFlight().Add(-1);
                }

                var responseFeature = features.Get<IHttpResponseFeature>();
                responseFeature?.StatusCode = 500;
                CompleteResponseBody(features);
                FireOnCompleted(features);
                Emit(features);
            }

            TryPullNext();
        }

        private void DispatchAsync(IFeatureCollection features, int seq)
        {
            TContext appContext;
            try
            {
                appContext = _stage._application.CreateContext(features);
                _appContexts[seq] = appContext;
            }
            catch (Exception)
            {
                _inFlight--;
                var responseFeature = features.Get<IHttpResponseFeature>();
                responseFeature?.StatusCode = 500;
                CompleteResponseBody(features);
                FireOnCompleted(features);
                Emit(features);
                return;
            }

            var task = _stage._application.ProcessRequestAsync(appContext);

            if (task.IsCompletedSuccessfully)
            {
                _inFlight--;
                _stage._application.DisposeContext(appContext, null);
                _appContexts.Remove(seq);
                CompleteResponseBody(features);
                FireOnCompleted(features);
                Emit(features);
            }
            else if (task.IsFaulted)
            {
                _inFlight--;
                var responseFeature = features.Get<IHttpResponseFeature>();
                responseFeature?.StatusCode = 500;
                _stage._application.DisposeContext(appContext, task.Exception);
                _appContexts.Remove(seq);
                CompleteResponseBody(features);
                FireOnCompleted(features);
                Emit(features);
            }
            else
            {
                var lifetime = features.Get<IHttpRequestLifetimeFeature>();
                var cts = lifetime is not null
                    ? CancellationTokenSource.CreateLinkedTokenSource(lifetime.RequestAborted)
                    : new CancellationTokenSource();
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
                _timerKeys[seq] = (softKey, hardKey);
                _activeTimeouts[seq] = cts;
                _activeFeatures[seq] = features;
                ScheduleOnce(softKey, _stage._handlerTimeout);

                var bodyFeature = features.Get<IHttpResponseBodyFeature>() as TurboHttpResponseBodyFeature;
                bodyFeature?.UpgradeToPipe();
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
                    if (handlerTask.IsFaulted &&
                        features.Get<IHttpResponseBodyFeature>() is not TurboHttpResponseBodyFeature
                        {
                            HasStarted: true
                        })
                    {
                        var responseFeature = features.Get<IHttpResponseFeature>();
                        responseFeature?.StatusCode = 500;
                    }

                    if (handlerTask.IsCompleted)
                    {
                        CompleteResponseBody(features);
                        FireOnCompleted(features);
                        _inFlight--;
                        if (_metricsEnabled)
                        {
                            Metrics.PipelineInFlight().Add(-1);
                            ResetBackpressure();
                        }

                        CleanupTimeout(seq);
                        DisposeAppContext(seq, handlerTask.Exception);
                        Emit(features);
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
                    if (!_activeTimeouts.ContainsKey(seq))
                    {
                        break;
                    }

                    CompleteResponseBody(finishedFeatures);
                    FireOnCompleted(finishedFeatures);
                    _inFlight--;
                    if (_metricsEnabled)
                    {
                        Metrics.PipelineInFlight().Add(-1);
                        ResetBackpressure();
                    }

                    CleanupTimeout(seq);
                    DisposeAppContext(seq, null);
                    if (_upstreamFinished && _inFlight == 0)
                    {
                        CompleteStage();
                    }

                    break;

                case HandlerFaulted(var seq, var faultedFeatures, var error):
                    if (!_activeTimeouts.ContainsKey(seq))
                    {
                        break;
                    }

                    CompleteResponseBody(faultedFeatures);
                    FireOnCompleted(faultedFeatures);
                    _inFlight--;
                    if (_metricsEnabled)
                    {
                        Metrics.PipelineInFlight().Add(-1);
                        ResetBackpressure();
                    }

                    CleanupTimeout(seq);
                    DisposeAppContext(seq, error);
                    if (_upstreamFinished && _inFlight == 0)
                    {
                        CompleteStage();
                    }

                    break;

                case DispatchCompleted(var seq, var features):
                    if (!_activeTimeouts.ContainsKey(seq))
                    {
                        break;
                    }

                    _inFlight--;
                    if (_metricsEnabled)
                    {
                        Metrics.PipelineInFlight().Add(-1);
                        ResetBackpressure();
                    }

                    CleanupTimeout(seq);
                    DisposeAppContext(seq, null);
                    CompleteResponseBody(features);
                    FireOnCompleted(features);
                    Emit(features);
                    break;

                case DispatchFailed(var seq, var features, var error):
                    if (!_activeTimeouts.ContainsKey(seq))
                    {
                        break;
                    }

                    _inFlight--;
                    if (_metricsEnabled)
                    {
                        Metrics.PipelineInFlight().Add(-1);
                        ResetBackpressure();
                    }

                    CleanupTimeout(seq);
                    DisposeAppContext(seq, error);
                    var respFeature = features.Get<IHttpResponseFeature>();
                    respFeature?.StatusCode = 500;
                    CompleteResponseBody(features);
                    FireOnCompleted(features);
                    Emit(features);
                    break;
            }

            if (_upstreamFinished && _inFlight == 0 && _pending.Count == 0)
            {
                CompleteStage();
            }
        }

        private void DisposeAppContext(int seq, Exception? exception)
        {
            if (_appContexts.TryGetValue(seq, out var appCtx))
            {
                _stage._application.DisposeContext(appCtx, exception);
                _appContexts.Remove(seq);
            }
        }

        private void CleanupTimeout(int seq)
        {
            if (_timerKeys.Remove(seq, out var timerKeys))
            {
                CancelTimer(timerKeys.Soft);
                CancelTimer(timerKeys.Hard);
            }

            _gracePhase.Remove(seq);
            _activeFeatures.Remove(seq);
            if (_activeTimeouts.Remove(seq, out var cts))
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
            var bodyFeature = features.Get<IHttpResponseBodyFeature>() as TurboHttpResponseBodyFeature;
            bodyFeature?.Complete();
        }

        private static void FireOnCompleted(IFeatureCollection features)
        {
            if (features.Get<IHttpResponseFeature>() is TurboHttpResponseFeature responseFeature)
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
            foreach (var (_, features) in _activeFeatures)
            {
                if (features.Get<IHttpRequestLifetimeFeature>() is TurboHttpRequestLifetimeFeature lifetime)
                {
                    lifetime.Abort();
                }

                CompleteResponseBody(features);
            }

            foreach (var (_, cts) in _activeTimeouts)
            {
                cts.Cancel();
                cts.Dispose();
            }

            foreach (var (_, appCtx) in _appContexts)
            {
                _stage._application.DisposeContext(appCtx, null);
            }

            _activeFeatures.Clear();
            _activeTimeouts.Clear();
            _appContexts.Clear();
            _timerKeys.Clear();
        }
    }
}
