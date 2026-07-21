using System.IO.Pipelines;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Protocol;
using static Servus.Senf;

namespace GaudiHTTP.Streams.Stages.Client;

internal sealed class HttpClientConnectionStageLogic<TSM> : TimerGraphStageLogic, IClientStageOperations
    where TSM : IClientStateMachine
{
    private const string TraceCategory = "Stage";
    private const string DrainCompleteTimerKey = "drain-complete";

    private readonly Inlet<ITransportInbound> _inNetwork;
    private readonly Outlet<HttpResponseMessage> _outResponse;
    private readonly Inlet<HttpRequestMessage> _inRequest;
    private readonly Outlet<ITransportOutbound> _outNetwork;

    private readonly TSM _sm;
    private readonly Queue<ITransportOutbound> _outboundQueue = new(64);
    private readonly Queue<HttpResponseMessage> _responseQueue = new(64);
    private readonly Dictionary<HttpRequestMessage, CancellationTokenRegistration> _ctRegistrations = new();
    private IActorRef _stageActor = ActorRefs.Nobody;
    private Action<HttpRequestMessage>? _cancelCallback;

    public HttpClientConnectionStageLogic(
        GraphStage<ClientConnectionShape> stage,
        Func<IClientStageOperations, TSM> smFactory) : base(stage.Shape)
    {
        var shape = stage.Shape;
        _inNetwork = shape.InNetwork;
        _outResponse = shape.OutResponse;
        _inRequest = shape.InRequest;
        _outNetwork = shape.OutNetwork;

        _sm = smFactory(this);

        SetHandler(_inNetwork, onPush: OnNetworkPush,
            onUpstreamFinish: () =>
            {
                Tracing.For(TraceCategory).Info(this, "network upstream finished (connection closed)");
                CloseAllPorts();
            },
            onUpstreamFailure: ex =>
            {
                Tracing.For(TraceCategory).Warning(this, "network upstream failure: {0}", ex.Message);
                CloseAllPorts();
            });

        SetHandler(_outResponse,
            onPull: () =>
            {
                if (_responseQueue.Count > 0)
                {
                    Push(_outResponse, _responseQueue.Dequeue());
                    return;
                }

                if (TryPullNetwork())
                {
                    Tracing.For(TraceCategory).Debug(this, "response outlet pull → pulling _inNetwork");
                }
            },
            onDownstreamFinish: cause =>
            {
                Tracing.For(TraceCategory).Info(this, "response downstream finished: {0}",
                    cause?.Message ?? "normal");
                CloseAllPorts();
            });

        SetHandler(_inRequest, onPush: () =>
            {
                var request = Grab(_inRequest);
                try
                {
                    _sm.OnRequest(request);

                    var ct = request.GetCancellationToken();
                    if (ct.CanBeCanceled)
                    {
                        var reg = ct.UnsafeRegister(
                            static (state, _) =>
                            {
                                var (cb, req) = ((Action<HttpRequestMessage>, HttpRequestMessage))state!;
                                cb(req);
                            },
                            (_cancelCallback!, request));
                        _ctRegistrations[request] = reg;
                    }
                }
                catch (Exception ex)
                {
                    Tracing.For(TraceCategory).Error(this, "OnRequest threw: {0}", ex.Message);
                    request.Fail(ex);
                }

                TryPullRequest();
            },
            onUpstreamFinish: () =>
            {
                Tracing.For(TraceCategory).Debug(this, "request upstream finished (inFlight={0}, reconnecting={1})",
                    _sm.HasInFlightRequests, _sm.IsReconnecting);
                if (!_sm.HasInFlightRequests && !_sm.IsReconnecting)
                {
                    CloseAllPorts();
                }
            },
            onUpstreamFailure: ex =>
            {
                Tracing.For(TraceCategory).Warning(this, "request upstream failure: {0}", ex.Message);
                if (!_sm.HasInFlightRequests && !_sm.IsReconnecting)
                {
                    CloseAllPorts();
                }
            });

        SetHandler(_outNetwork,
            onPull: OnNetworkPull,
            onDownstreamFinish: cause =>
            {
                Tracing.For(TraceCategory).Info(this, "network downstream finished: {0}",
                    cause?.Message ?? "normal");
                CloseAllPorts();
            });
    }

    public override void PreStart()
    {
        _stageActor = GetStageActor(OnStageActorMessage).Ref;
        _cancelCallback = GetAsyncCallback<HttpRequestMessage>(OnRequestCancelled);
        _sm.PreStart();
    }

    private void OnStageActorMessage((IActorRef sender, object message) args)
    {
        Tracing.For(TraceCategory).Debug(this, "actor msg: {0}, pause={1}", args.message.GetType().Name,
            _sm.ShouldPauseNetwork);
        _sm.OnBodyMessage(args.message);

        var pauseAfter = _sm.ShouldPauseNetwork;
        var pulled = HasBeenPulled(_inNetwork);
        var closed = IsClosed(_inNetwork);
        Tracing.For(TraceCategory)
            .Debug(this, "after msg: pause={0}, pulled={1}, closed={2}", pauseAfter, pulled, closed);

        if (TryPullNetwork())
        {
            Tracing.For(TraceCategory).Debug(this, "re-pull _inNetwork after body message");
        }

        TryPullRequest();
        TryCompleteAfterAllResponses();
    }

    private void OnNetworkPush()
    {
        Tracing.For(TraceCategory).Debug(this, "network push");
        var item = Grab(_inNetwork);

        try
        {
            _sm.DecodeServerData(item);
        }
        catch (Exception ex)
        {
            Tracing.For(TraceCategory).Error(this, "DecodeServerData threw — failing connection: {0}", ex);
            FailStage(ex);
            return;
        }

        if (_responseQueue.Count > 0)
        {
            TryPushResponse();
        }

        TryPullNetwork();

        TryPullRequest();
        TryCompleteAfterAllResponses();
    }

    private void OnNetworkPull()
    {
        if (_outboundQueue.Count > 0)
        {
            Push(_outNetwork, _outboundQueue.Dequeue());
            TryCompleteAfterAllResponses();
            return;
        }

        TryPullRequest();
    }

    protected override void OnTimer(object timerKey)
    {
        if (timerKey is not string name)
        {
            return;
        }

        if (name == DrainCompleteTimerKey)
        {
            if (IsFullyDrained)
            {
                Tracing.For(TraceCategory).Debug(this, "drain complete — closing stage");
                CompleteStage();
            }

            return;
        }

        Tracing.For(TraceCategory).Trace(this, "timer fired: {0}", name);
        _sm.OnTimerFired(name);
    }

    void IClientStageOperations.OnResponse(HttpResponseMessage response)
    {
        if (response.RequestMessage is not null && _ctRegistrations.Remove(response.RequestMessage, out var reg))
        {
            reg.Dispose();
        }

        if (IsAvailable(_outResponse))
        {
            Push(_outResponse, response);
            return;
        }

        _responseQueue.Enqueue(response);
    }

    void IClientStageOperations.OnOutbound(ITransportOutbound item)
    {
        if (IsAvailable(_outNetwork))
        {
            Push(_outNetwork, item);
            return;
        }

        _outboundQueue.Enqueue(item);
    }

    void IClientStageOperations.OnScheduleTimer(string name, TimeSpan duration) => ScheduleOnce(name, duration);

    void IClientStageOperations.OnCancelTimer(string name) => CancelTimer(name);

    IActorRef IClientStageOperations.StageActor => _stageActor;

    bool IClientStageOperations.HasPendingDemand => _outboundQueue.Count == 0 && IsAvailable(_outNetwork);

    private void OnRequestCancelled(HttpRequestMessage request)
    {
        if (_ctRegistrations.Remove(request, out var reg))
        {
            reg.Dispose();
        }
        _sm.OnRequestCancelled(request);
    }

    private void TryPushResponse()
    {
        if (_responseQueue.Count > 0 && IsAvailable(_outResponse))
        {
            Push(_outResponse, _responseQueue.Dequeue());
        }
    }

    private void TryPullRequest()
    {
        if (_sm.CanAcceptRequest
            && !HasBeenPulled(_inRequest)
            && !IsClosed(_inRequest))
        {
            Pull(_inRequest);
        }
    }

    private bool IsFullyDrained =>
        IsClosed(_inRequest)
        && !_sm.HasInFlightRequests
        && !_sm.IsReconnecting
        && _responseQueue.Count == 0
        && _outboundQueue.Count == 0;

    private void TryCompleteAfterAllResponses()
    {
        if (IsFullyDrained && !IsTimerActive(DrainCompleteTimerKey))
        {
            ScheduleOnce(DrainCompleteTimerKey, TimeSpan.FromMilliseconds(100));
        }
    }

    private bool TryPullNetwork()
    {
        if (!_sm.ShouldPauseNetwork && !HasBeenPulled(_inNetwork) && !IsClosed(_inNetwork))
        {
            Pull(_inNetwork);
            return true;
        }

        return false;
    }

    private void CloseAllPorts()
    {
        _sm.OnUpstreamFinished();

        if (!IsClosed(_outResponse))
        {
            Complete(_outResponse);
        }

        if (!IsClosed(_inRequest))
        {
            Cancel(_inRequest);
        }

        if (!IsClosed(_outNetwork))
        {
            Complete(_outNetwork);
        }

        if (!IsClosed(_inNetwork))
        {
            Cancel(_inNetwork);
        }
    }

    public override void PostStop()
    {
        foreach (var reg in _ctRegistrations.Values)
        {
            reg.Dispose();
        }
        _ctRegistrations.Clear();

        Tracing.For(TraceCategory).Debug(this, "PostStop: draining {0} outbound, {1} responses",
            _outboundQueue.Count, _responseQueue.Count);
        _outboundQueue.Clear();

        while (_responseQueue.Count > 0)
        {
            _responseQueue.Dequeue().Dispose();
        }

        _sm.Cleanup();
    }
}

