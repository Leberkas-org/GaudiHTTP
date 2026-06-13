using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Diagnostics;
using TurboHTTP.Protocol;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using static Servus.Senf;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class HttpConnectionServerStageLogic<TSM> : TimerGraphStageLogic, IServerStageOperations
    where TSM : IServerStateMachine
{
    private const string TraceCategory = "Stage";
    private readonly Inlet<ITransportInbound> _inNetwork;
    private readonly Outlet<IFeatureCollection> _outRequest;
    private readonly Inlet<IFeatureCollection> _inResponse;
    private readonly Outlet<ITransportOutbound> _outNetwork;

    private readonly TSM _sm;
    private readonly Queue<IFeatureCollection> _requestQueue = new();
    private readonly Queue<ITransportOutbound> _outboundQueue = new();
    private bool _completeAfterFlush;

    // Requests pushed to the handler (_outRequest) minus responses received on _inResponse. The
    // stage refuses to dispatch more than the state machine's MaxConcurrentRequests concurrently;
    // HTTP/1.x reports 1, which serializes pipelined dispatch so the shared, completion-ordered
    // ApplicationBridgeStage can never reorder responses (RFC 9112 §9.3.2). Multiplexed protocols
    // leave the limit unbounded, so this counter never gates them.
    private int _handlerInFlight;
    private IActorRef _stageActor = ActorRefs.Nobody;
    private readonly IServiceProvider? _services;
    private TurboHttpConnectionFeature? _connectionFeature;
    private TlsHandshakeFeature? _tlsHandshakeFeature;
    private readonly bool _metricsEnabled;
    private readonly int _maxCoalesce;
    private Activity? _connectionActivity;
    private long _connectionTimestamp;

    public HttpConnectionServerStageLogic(
        GraphStage<ServerConnectionShape> stage,
        Func<IServerStageOperations, TSM> smFactory,
        IServiceProvider? services = null,
        int maxCoalesce = 8) : base(stage.Shape)
    {
        var shape = stage.Shape;
        _inNetwork = shape.InNetwork;
        _outRequest = shape.OutRequest;
        _inResponse = shape.InResponse;
        _outNetwork = shape.OutNetwork;
        _services = services;

        _sm = smFactory(this);
        _maxCoalesce = maxCoalesce;
        _metricsEnabled = Metrics.ServerActiveRequests().Enabled
            || Metrics.ServerRequestDuration().Enabled
            || Tracing.IsServerTracingActive();

        SetHandler(_inNetwork,
            onPush: OnNetworkPush,
            onUpstreamFinish: () =>
            {
                Tracing.For(TraceCategory).Debug(this, "network upstream finished");
                _sm.OnDownstreamFinished();
                CompleteStage();
            },
            onUpstreamFailure: ex =>
            {
                Tracing.For(TraceCategory).Info(this, "network upstream failure: {0}", ex.Message);
                _sm.OnDownstreamFinished();
                if (!IsClosed(_outRequest))
                {
                    Complete(_outRequest);
                }

                if (!IsClosed(_inResponse))
                {
                    Cancel(_inResponse);
                }

                if (!IsClosed(_outNetwork))
                {
                    Complete(_outNetwork);
                }
            });

        SetHandler(_outRequest, onPull: () =>
        {
            if (_requestQueue.Count > 0)
            {
                if (CanDispatch)
                {
                    Push(_outRequest, _requestQueue.Dequeue());
                    _handlerInFlight++;
                }

                // Otherwise the handler is busy: leave the demand outstanding so a completing
                // response releases the next queued request via TryPushRequest. OnNetworkPush keeps
                // reading the wire ahead independently, so requests still drain off the socket.
                return;
            }

            if (!HasBeenPulled(_inNetwork) && !IsClosed(_inNetwork))
            {
                Pull(_inNetwork);
            }
        });

        SetHandler(_inResponse,
            onPush: () =>
            {
                var response = Grab(_inResponse);
                if (_handlerInFlight > 0)
                {
                    _handlerInFlight--;
                }

                try
                {
                    _sm.OnResponse(response);
                }
                catch (Exception ex)
                {
                    Tracing.For(TraceCategory).Error(this, "OnResponse threw: {0}", ex.Message);
                }

                if (_sm.ShouldComplete)
                {
                    if (_metricsEnabled)
                    {
                        OnResponseInstrumented(response);
                    }
                    Tracing.For(TraceCategory).Debug(this, "completing after response (connection close)");
                    CompleteStage();
                    return;
                }

                if (_metricsEnabled)
                {
                    OnResponseInstrumented(response);
                }

                var bodyFeature = response.Get<IHttpResponseBodyFeature>();
                var hasBody = bodyFeature is not null;
                if (!hasBody)
                {
                    FeatureCollectionFactory.Return(response);
                }

                // A handler slot just freed: release the next pipelined request (a no-op for
                // multiplexed protocols, whose queue is already drained) before pulling the
                // following response.
                TryPushRequest();
                TryPullResponse();
            },
            onUpstreamFinish: () =>
            {
                Tracing.For(TraceCategory).Debug(this, "response upstream finished");
                CompleteStage();
            },
            onUpstreamFailure: _ =>
            {
                _sm.OnDownstreamFinished();
                if (!IsClosed(_outRequest))
                {
                    Complete(_outRequest);
                }

                if (!IsClosed(_inNetwork))
                {
                    Cancel(_inNetwork);
                }

                if (!IsClosed(_outNetwork))
                {
                    Complete(_outNetwork);
                }
            });

        SetHandler(_outNetwork,
            onPull: OnNetworkPull,
            onDownstreamFinish: _ =>
            {
                _sm.OnDownstreamFinished();
                if (!IsClosed(_outRequest))
                {
                    Complete(_outRequest);
                }

                if (!IsClosed(_inResponse))
                {
                    Cancel(_inResponse);
                }

                if (!IsClosed(_inNetwork))
                {
                    Cancel(_inNetwork);
                }
            });
    }

    public override void PreStart()
    {
        _stageActor = GetStageActor(OnStageActorMessage).Ref;
        _sm.PreStart();
        Pull(_inNetwork);
    }

    private void OnStageActorMessage((IActorRef sender, object message) args)
    {
        if (args.message is BodyResumed)
        {
            Tracing.For(TraceCategory).Trace(this, "body resumed");
            _sm.ResumeBody();
            if (!_sm.ShouldPauseNetwork && !HasBeenPulled(_inNetwork) && !IsClosed(_inNetwork))
            {
                Pull(_inNetwork);
            }

            return;
        }

        Tracing.For(TraceCategory).Trace(this, "body message: {0}", args.message.GetType().Name);
        _sm.OnBodyMessage(args.message);
        TryPushOutbound();
        TryPullResponse();
    }

    private void OnNetworkPush()
    {
        var item = Grab(_inNetwork);

        if (item is TransportConnected connected)
        {
            var info = connected.Info;
            if (info.Remote is IPEndPoint remoteEp)
            {
                var connectionFeature = new TurboHttpConnectionFeature
                {
                    ConnectionId = Guid.NewGuid().ToString("N"),
                    RemoteIpAddress = remoteEp.Address,
                    RemotePort = remoteEp.Port,
                    LocalIpAddress = (info.Local as IPEndPoint)?.Address,
                    LocalPort = (info.Local as IPEndPoint)?.Port ?? 0
                };

                if (info.Security is { } security)
                {
                    _tlsHandshakeFeature = new TlsHandshakeFeature
                    {
                        Protocol = security.Protocol,
                        NegotiatedCipherSuite = security.NegotiatedCipherSuite,
                        HostName = security.HostName,
                        NegotiatedApplicationProtocol = security.ApplicationProtocol
                    };
                }

                _connectionFeature = connectionFeature;

                if (_metricsEnabled)
                {
                    OnConnectionEstablished(connectionFeature, info.Security is not null ? "tls" : "tcp");
                }
            }
        }

        try
        {
            _sm.DecodeClientData(item);
        }
        catch (Exception ex)
        {
            Tracing.For(TraceCategory).Warning(this, "DecodeClientData threw: {0}", ex.Message);
        }

        // The state machine signals a connection-fatal error by enqueuing a GOAWAY and setting
        // ShouldComplete. Flush the GOAWAY to the network, then close the connection.
        if (_sm.ShouldComplete)
        {
            CompleteAfterFlushingOutbound();
            return;
        }

        if (_requestQueue.Count > 0)
        {
            TryPushRequest();
        }

        if (!_sm.ShouldPauseNetwork && !HasBeenPulled(_inNetwork) && !IsClosed(_inNetwork))
        {
            Pull(_inNetwork);
        }

        TryPullResponse();
    }

    private void OnNetworkPull()
    {
        if (_outboundQueue.Count > 0)
        {
            PushOutbound();
        }

        TryPullResponse();
    }

    protected override void OnTimer(object timerKey)
    {
        if (timerKey is string name)
        {
            _sm.OnTimerFired(name);

            // If the state machine signals termination (data-rate violation, keep-alive timeout, etc.),
            // abort the connection immediately. For H2/H3, ShouldComplete is always false, so this is safe.
            if (_sm.ShouldComplete)
            {
                Tracing.For(TraceCategory).Info(this, "timer '{0}' triggered connection close", name);
                CompleteStage();
            }
        }
    }

    void IServerStageOperations.OnRequest(IFeatureCollection features)
    {
        if (_requestQueue.Count >= _sm.MaxQueuedRequests)
        {
            Log.Warning("Request queue exceeded {0}, closing connection", _sm.MaxQueuedRequests);
            CompleteStage();
            return;
        }

        if (_metricsEnabled)
        {
            OnRequestInstrumented(features);
        }

        _requestQueue.Enqueue(features);
        TryPushRequest();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OnRequestInstrumented(IFeatureCollection features)
    {
        var requestFeature = features.Get<IHttpRequestFeature>();
        if (requestFeature is null)
        {
            return;
        }

        var method = requestFeature.Method;
        var path = requestFeature.Path;
        var scheme = requestFeature.Scheme;

        if (Metrics.ServerActiveRequests().Enabled)
        {
            var tags = new TagList
            {
                { "url.scheme", scheme },
                { "http.request.method", TurboClientInstrumentationExtensions.NormalizeMethod(method) }
            };
            Metrics.ServerActiveRequests().Add(1, tags);
        }

        if (features is TurboFeatureCollection turbo)
        {
            turbo.RequestTimestamp = Stopwatch.GetTimestamp();
            var headers = requestFeature.Headers;
            turbo.RequestActivity = Tracing.StartRequestActivity(method, path, scheme, headers.TraceParent, headers.TraceState);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OnResponseInstrumented(IFeatureCollection features)
    {
        var responseFeature = features.Get<IHttpResponseFeature>();
        var requestFeature = features.Get<IHttpRequestFeature>();
        var statusCode = responseFeature?.StatusCode ?? 0;

        if (requestFeature is not null && Metrics.ServerActiveRequests().Enabled)
        {
            var tags = new TagList
            {
                { "url.scheme", requestFeature.Scheme },
                { "http.request.method", TurboClientInstrumentationExtensions.NormalizeMethod(requestFeature.Method) }
            };
            Metrics.ServerActiveRequests().Add(-1, tags);
        }

        if (features is TurboFeatureCollection turbo)
        {
            if (turbo.RequestActivity is { } activity)
            {
                Tracing.SetServerResponse(activity, statusCode);
                activity.Stop();
            }

            if (turbo.RequestTimestamp > 0 && Metrics.ServerRequestDuration().Enabled && requestFeature is not null)
            {
                var elapsed = Stopwatch.GetElapsedTime(turbo.RequestTimestamp);
                var durationTags = new TagList
                {
                    { "http.request.method", TurboClientInstrumentationExtensions.NormalizeMethod(requestFeature.Method) },
                    { "http.response.status_code", statusCode },
                    { "url.scheme", requestFeature.Scheme }
                };
                Metrics.ServerRequestDuration().Record(elapsed.TotalSeconds, durationTags);
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnConnectionEstablished(TurboHttpConnectionFeature conn, string transport)
    {
        _connectionTimestamp = Stopwatch.GetTimestamp();
        Metrics.ActiveConnections().Add(1);

        var localAddr = conn.LocalIpAddress?.ToString() ?? "unknown";
        var localPort = conn.LocalPort;
        _connectionActivity = Tracing.StartConnectionActivity(localAddr, localPort, transport);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnConnectionClosed()
    {
        Metrics.ActiveConnections().Add(-1);

        if (_connectionTimestamp > 0)
        {
            var elapsed = Stopwatch.GetElapsedTime(_connectionTimestamp);
            Metrics.ConnectionDuration().Record(elapsed.TotalSeconds);
        }

        if (_connectionActivity is { } activity)
        {
            Tracing.StopConnectionActivity(activity, error: null);
            _connectionActivity = null;
        }
    }

    void IServerStageOperations.OnOutbound(ITransportOutbound item)
    {
        _outboundQueue.Enqueue(item);
        TryPushOutbound();
    }

    void IServerStageOperations.OnScheduleTimer(string name, TimeSpan delay)
        => ScheduleOnce(name, delay);

    void IServerStageOperations.OnCancelTimer(string name)
        => CancelTimer(name);

    ILoggingAdapter IServerStageOperations.Log => Log;

    IActorRef IServerStageOperations.StageActor => _stageActor;

    IMaterializer IServerStageOperations.Materializer => Materializer;

    IServiceProvider? IServerStageOperations.Services => _services;

    TurboHttpConnectionFeature? IServerStageOperations.ConnectionFeature => _connectionFeature;

    TlsHandshakeFeature? IServerStageOperations.TlsHandshakeFeature => _tlsHandshakeFeature;

    void IServerStageOperations.OnResponseBodyComplete(IFeatureCollection features)
    {
        FeatureCollectionFactory.Return(features);
    }

    private bool CanDispatch => _handlerInFlight < _sm.MaxConcurrentRequests;

    private void TryPushRequest()
    {
        if (_requestQueue.Count > 0 && IsAvailable(_outRequest) && CanDispatch)
        {
            Push(_outRequest, _requestQueue.Dequeue());
            _handlerInFlight++;
        }
    }

    private void TryPushOutbound()
    {
        if (_outboundQueue.Count > 0 && IsAvailable(_outNetwork))
        {
            PushOutbound();
        }
    }

    private void PushOutbound()
    {
        if (_outboundQueue.Count == 1 || !TryCoalesceOutbound(out var flushedCount))
        {
            Push(_outNetwork, _outboundQueue.Dequeue());
            flushedCount = 1;
        }

        for (var i = 0; i < flushedCount; i++)
        {
            _sm.OnOutboundFlushed();
        }

        if (_completeAfterFlush && _outboundQueue.Count == 0)
        {
            CompleteStage();
        }
    }

    private void CompleteAfterFlushingOutbound()
    {
        _completeAfterFlush = true;

        if (_outboundQueue.Count == 0)
        {
            CompleteStage();
            return;
        }

        // Push now if the network outlet has demand; otherwise the next OnNetworkPull drains the
        // queue and PushOutbound completes the stage once the GOAWAY has been emitted.
        TryPushOutbound();
    }

    private bool TryCoalesceOutbound(out int coalescedCount)
    {
        coalescedCount = 0;
        var totalSize = 0;
        var maxBytes = _maxCoalesce * 16 * 1024;

        foreach (var item in _outboundQueue)
        {
            if (item is not TransportData { Buffer: var buf })
            {
                break;
            }

            totalSize += buf.Length;
            coalescedCount++;
            if (totalSize >= maxBytes)
            {
                break;
            }
        }

        if (coalescedCount < 2)
        {
            return false;
        }

        var merged = TransportBuffer.Rent(totalSize);
        var dest = merged.FullMemory.Span;
        var offset = 0;

        for (var i = 0; i < coalescedCount; i++)
        {
            var item = _outboundQueue.Dequeue();
            if (item is TransportData td)
            {
                td.Buffer.Span.CopyTo(dest[offset..]);
                offset += td.Buffer.Length;
                td.Buffer.Dispose();
                td.Return();
            }
        }

        merged.Length = offset;
        Push(_outNetwork, TransportData.Rent(merged));
        return true;
    }

    private void TryPullResponse()
    {
        if (_sm.CanAcceptResponse
            && !HasBeenPulled(_inResponse)
            && !IsClosed(_inResponse))
        {
            Pull(_inResponse);
        }
    }

    public override void PostStop()
    {
        Tracing.For(TraceCategory).Debug(this, "PostStop: draining {0} outbound, {1} requests",
            _outboundQueue.Count, _requestQueue.Count);

        if (_metricsEnabled)
        {
            OnConnectionClosed();
        }

        while (_outboundQueue.Count > 0)
        {
            if (_outboundQueue.Dequeue() is TransportData td)
            {
                td.Buffer.Dispose();
                td.Return();
            }
        }

        while (_requestQueue.Count > 0)
        {
            _requestQueue.Dequeue();
        }

        _sm.Cleanup();
    }
}