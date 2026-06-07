using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using static Servus.Senf;

namespace TurboHTTP.Streams.Stages.Client;

internal sealed class HttpConnectionStageLogic<TSM> : TimerGraphStageLogic, IClientStageOperations
    where TSM : IClientStateMachine
{
    private const string TraceCategory = "Stage";

    private readonly Inlet<ITransportInbound> _inServer;
    private readonly Outlet<HttpResponseMessage> _outResponse;
    private readonly Inlet<HttpRequestMessage> _inApp;
    private readonly Outlet<ITransportOutbound> _outNetwork;

    private readonly TSM _sm;
    private readonly Queue<ITransportOutbound> _outboundQueue = new(64);
    private readonly Queue<HttpResponseMessage> _responseQueue = new(64);
    private IActorRef _stageActor = ActorRefs.Nobody;

    public HttpConnectionStageLogic(
        GraphStage<ClientConnectionShape> stage,
        Func<IClientStageOperations, TSM> smFactory) : base(stage.Shape)
    {
        var shape = stage.Shape;
        _inServer = shape.InNetwork;
        _outResponse = shape.OutResponse;
        _inApp = shape.InRequest;
        _outNetwork = shape.OutNetwork;

        _sm = smFactory(this);

        SetHandler(_inServer, onPush: OnServerPush,
            onUpstreamFinish: () =>
            {
                Tracing.For(TraceCategory).Debug(this, "server upstream finished");
                _sm.OnUpstreamFinished();
                CompleteStage();
            },
            onUpstreamFailure: ex =>
            {
                Tracing.For(TraceCategory).Info(this, "server upstream failure: {0}", ex.Message);
                _sm.OnUpstreamFinished();
                CompleteStage();
            });

        SetHandler(_outResponse, onPull: () =>
        {
            if (_responseQueue.Count > 0)
            {
                Push(_outResponse, _responseQueue.Dequeue());
                return;
            }

            if (!_sm.ShouldPauseNetwork && !HasBeenPulled(_inServer) && !IsClosed(_inServer))
            {
                Tracing.For(TraceCategory).Debug(this, "response outlet pull → pulling _inServer");
                Pull(_inServer);
            }
        });

        SetHandler(_inApp, onPush: () =>
            {
                var request = Grab(_inApp);
                try
                {
                    _sm.OnRequest(request);
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
                    CompleteStage();
                }
            },
            onUpstreamFailure: _ => { _sm.OnUpstreamFinished(); });

        SetHandler(_outNetwork, onPull: OnNetworkPull);
    }

    public override void PreStart()
    {
        _stageActor = GetStageActor(OnStageActorMessage).Ref;
        _sm.PreStart();
    }

    private void OnStageActorMessage((IActorRef sender, object message) args)
    {
        Tracing.For(TraceCategory).Debug(this, "actor msg: {0}, pause={1}", args.message.GetType().Name,
            _sm.ShouldPauseNetwork);
        _sm.OnBodyMessage(args.message);

        var pauseAfter = _sm.ShouldPauseNetwork;
        var pulled = HasBeenPulled(_inServer);
        var closed = IsClosed(_inServer);
        Tracing.For(TraceCategory)
            .Debug(this, "after msg: pause={0}, pulled={1}, closed={2}", pauseAfter, pulled, closed);

        if (!pauseAfter && !pulled && !closed)
        {
            Tracing.For(TraceCategory).Debug(this, "re-pull _inServer after body message");
            Pull(_inServer);
        }

        TryPullRequest();
        TryCompleteAfterAllResponses();
    }

    private void OnServerPush()
    {
        Tracing.For(TraceCategory).Debug(this, "server push");
        var item = Grab(_inServer);
        try
        {
            _sm.DecodeServerData(item);
        }
        catch (Exception ex)
        {
            Tracing.For(TraceCategory).Warning(this, "DecodeServerData threw: {0}", ex.Message);
        }

        if (_responseQueue.Count > 0)
        {
            TryPushResponse();
        }

        if (!_sm.ShouldPauseNetwork && !HasBeenPulled(_inServer) && !IsClosed(_inServer))
        {
            Pull(_inServer);
        }

        TryPullRequest();
        TryCompleteAfterAllResponses();
    }

    private void OnNetworkPull()
    {
        if (_outboundQueue.Count > 0)
        {
            Push(_outNetwork, _outboundQueue.Dequeue());
            _sm.OnOutboundFlushed();
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
            if (IsClosed(_inApp)
                && !_sm.HasInFlightRequests
                && !_sm.IsReconnecting
                && _responseQueue.Count == 0
                && _outboundQueue.Count == 0)
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
            _sm.OnOutboundFlushed();
            return;
        }

        _outboundQueue.Enqueue(item);
    }

    void IClientStageOperations.OnScheduleTimer(string name, TimeSpan duration) => ScheduleOnce(name, duration);

    void IClientStageOperations.OnCancelTimer(string name) => CancelTimer(name);

    IActorRef IClientStageOperations.StageActor => _stageActor;

    private void TryPushResponse()
    {
        if (_responseQueue.Count > 0 && IsAvailable(_outResponse))
        {
            Push(_outResponse, _responseQueue.Dequeue());
        }
    }

    private void TryPushOutbound()
    {
        if (_outboundQueue.Count == 0 || !IsAvailable(_outNetwork))
        {
            return;
        }

        if (_outboundQueue.Count == 1)
        {
            Push(_outNetwork, _outboundQueue.Dequeue());
            return;
        }

        if (!TryCoalesceOutbound())
        {
            Push(_outNetwork, _outboundQueue.Dequeue());
        }
    }

    private bool TryCoalesceOutbound()
    {
        var totalSize = 0;
        var coalesceCount = 0;
        const int maxCoalesce = 8;

        foreach (var item in _outboundQueue)
        {
            if (item is not TransportData { Buffer: var buf })
            {
                break;
            }

            totalSize += buf.Length;
            coalesceCount++;
            if (coalesceCount >= maxCoalesce)
            {
                break;
            }
        }

        if (coalesceCount < 2)
        {
            return false;
        }

        var merged = TransportBuffer.Rent(totalSize);
        var dest = merged.FullMemory.Span;
        var offset = 0;

        for (var i = 0; i < coalesceCount; i++)
        {
            var item = _outboundQueue.Dequeue();
            if (item is TransportData { Buffer: var buf })
            {
                buf.Span.CopyTo(dest[offset..]);
                offset += buf.Length;
                buf.Dispose();
            }
        }

        merged.Length = offset;
        Push(_outNetwork, new TransportData(merged));
        return true;
    }

    private void TryPullRequest()
    {
        if (_sm.CanAcceptRequest
            && !HasBeenPulled(_inApp)
            && !IsClosed(_inApp))
        {
            Pull(_inApp);
        }
    }

    private const string DrainCompleteTimerKey = "drain-complete";

    private void TryCompleteAfterAllResponses()
    {
        if (IsClosed(_inApp)
            && !_sm.HasInFlightRequests
            && !_sm.IsReconnecting
            && _responseQueue.Count == 0
            && _outboundQueue.Count == 0
            && !IsTimerActive(DrainCompleteTimerKey))
        {
            ScheduleOnce(DrainCompleteTimerKey, TimeSpan.FromMilliseconds(100));
        }
    }

    public override void PostStop()
    {
        Tracing.For(TraceCategory).Debug(this, "PostStop: draining {0} outbound, {1} responses", _outboundQueue.Count,
            _responseQueue.Count);
        while (_outboundQueue.Count > 0)
        {
            if (_outboundQueue.Dequeue() is TransportData { Buffer: var buffer })
            {
                buffer.Dispose();
            }
        }

        while (_responseQueue.Count > 0)
        {
            _responseQueue.Dequeue().Dispose();
        }

        _sm.Cleanup();
    }
}