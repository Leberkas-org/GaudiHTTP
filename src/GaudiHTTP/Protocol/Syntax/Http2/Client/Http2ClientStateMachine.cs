using System.Buffers;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Internal;
using GaudiHTTP.Protocol.Multiplexed;
using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Streams.Stages.Client;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Syntax.Http2.Client;

internal sealed class Http2ClientStateMachine :
    TcpStateMachineBase<IClientStageOperations>, IClientStateMachine
{
    private readonly GaudiClientOptions _options;
    private readonly Http2ClientSessionManager _clientSession;
    private readonly ReconnectionManager _reconnect;
    private TransportOptions? _transportOptions;
    private List<HttpRequestMessage>? _pendingRequests;

    private const string KeepAlivePingTimerKey = "keep-alive-ping";
    private const string KeepAlivePingTimeoutKey = "keep-alive-ping-timeout";

    private bool KeepAliveEnabled => _options.Http2.KeepAlivePingDelay != Timeout.InfiniteTimeSpan;

    public Http2ClientStateMachine(
        GaudiClientOptions options,
        IClientStageOperations ops,
        TimeProvider? timeProvider = null) : base(ops)
    {
        _options = options;
        _clientSession = new Http2ClientSessionManager(options, ops, timeProvider);
        _clientSession.EmitData = EmitToTransport;
        _reconnect = new ReconnectionManager(options.Http2.MaxReconnectAttempts, options.Http2.MaxReconnectBufferSize);
    }

    private void EmitToTransport(ReadOnlySpan<byte> data)
    {
        var mem = Transport!.GetMemory(data.Length);
        data.CopyTo(mem.Span);
        Transport.Advance(data.Length);
        RequestFlush();
    }

    protected override Akka.Actor.IActorRef Self => Ops.Self;
    protected override bool ShouldPauseReads => false;
    protected override void OnFlushCompleted() { }
    protected override void OnFlushDeferred() { }
    protected override void OnTransportLost(Exception? ex) => HandleTransportLost();
    protected override void OnTransportConnected(ConnectionInfo info) => OnConnectionRestored();
    protected override void OnTransportDisconnected(DisconnectReason reason)
    {
        if (_reconnect.IsReconnecting)
        {
            OnReconnectAttemptFailed();
        }
        else if (_clientSession.HasInFlightRequests)
        {
            OnConnectionLost(_clientSession.GoAwayReceived ? _clientSession.GoAwayLastStreamId : 0);
        }
    }

    public bool CanAcceptRequest =>
        !_clientSession.GoAwayReceived && !_reconnect.IsReconnecting && _clientSession.CanOpenStream;

    public bool HasInFlightRequests => _clientSession.HasInFlightRequests;
    public bool IsReconnecting => _reconnect.IsReconnecting;
    public RequestEndpoint Endpoint => _clientSession.Endpoint;
    public int ReconnectBufferCount => _reconnect.BufferedCount;

    public void PreStart()
    {
    }

    public void OnRequest(HttpRequestMessage request)
    {
        if (Transport is null && _clientSession.Endpoint != default)
        {
            (_pendingRequests ??= []).Add(request);
            return;
        }

        _clientSession.EncodeRequest(request);
    }

    public void DecodeServerData(ITransportInbound data)
    {
        if (DispatchLifecycleEvent(data))
        {
            return;
        }

        if (data is TransportData)
        {
            throw new InvalidOperationException("TransportData is not supported on TCP state machines; use pipe transport.");
        }
    }

    public void OnUpstreamFinished()
    {
        if (_reconnect.IsReconnecting)
        {
            _reconnect.FailAllBuffered(new HttpRequestException("HTTP/2 transport closed during reconnect."));
            _reconnect.Reset();
            Tracing.For("Protocol").Debug(this, "HTTP/2 transport closed during reconnect");
        }
    }

    public void OnTimerFired(string name)
    {
        switch (name)
        {
            case KeepAlivePingTimerKey:
                {
                    var policy = _options.Http2.KeepAlivePingPolicy;
                    if (policy == HttpKeepAlivePingPolicy.WithActiveRequests && !_clientSession.HasInFlightRequests)
                    {
                        return;
                    }

                    _clientSession.SendKeepAlivePing();
                    ScheduleKeepAlivePingTimeout();
                    break;
                }
            case KeepAlivePingTimeoutKey:
                {
                    if (_clientSession.IsKeepAliveTimedOut(_options.Http2.KeepAlivePingTimeout))
                    {
                        Tracing.For("Protocol").Info(this, "HTTP/2: Keep-alive PING timeout - closing connection");
                        if (_clientSession.HasInFlightRequests)
                        {
                            OnConnectionLost(lastStreamId: 0);
                        }
                    }

                    break;
                }
            case ReconnectBackoff.TimerName:
                {
                    if (_reconnect.IsReconnecting && _transportOptions is not null)
                    {
                        Ops.OnOutbound(new ConnectTransport(_transportOptions));
                    }

                    break;
                }
        }
    }

    public void OnRequestCancelled(HttpRequestMessage request)
    {
        if (IsReconnecting)
        {
            request.Fail(new OperationCanceledException("Request cancelled by caller."));
            return;
        }

        if (_clientSession.TryCancelStream(request))
        {
            Tracing.For("Protocol").Debug(this, "HTTP/2: cancelled request, sent RST_STREAM");
        }
    }


    public void OnBodyMessage(object msg)
    {
        if (TryHandleAsyncResult(msg))
        {
            return;
        }

        _clientSession.OnBodyMessage(msg);
    }

    public void Cleanup()
    {
        // Fail (don't silently drop) requests still in flight or buffered for reconnect replay, so
        // callers fault promptly on stage teardown instead of hanging until their client-side timeout.
        // request.Fail is idempotent, so streams that already delivered a response are unaffected.
        if (_reconnect.IsReconnecting)
        {
            _reconnect.FailAllBuffered(new HttpRequestException("HTTP/2 connection torn down during reconnect."));
            _reconnect.Reset();
        }

        foreach (var (_, request) in _clientSession.GetCorrelationMap())
        {
            request.Fail(new HttpRequestException("HTTP/2 connection was torn down before the request completed."));
        }

        CleanupTransportIo();
        _clientSession.Cleanup();
    }

    protected override (SequencePosition Consumed, SequencePosition Examined) DecodeData(ReadOnlySequence<byte> data)
    {
        int frameCount;
        SequencePosition consumed;
        try
        {
            var frames = _clientSession.DecodeFrames(in data, out consumed);
            frameCount = frames.Count;
            for (var i = 0; i < frames.Count; i++)
            {
                _clientSession.ProcessFrame(frames[i]);
            }
        }
        catch (HttpProtocolException ex)
        {
            Tracing.For("Protocol").Warning(this,
                "HTTP/2: connection protocol error - disconnecting: {0}", ex.Message);
            Ops.OnOutbound(new DisconnectTransport(DisconnectReason.Error));
            return (data.End, data.End);
        }

        if (_clientSession is { GoAwayReceived: true, HasInFlightRequests: true })
        {
            if (!_clientSession.GoAwayWasGraceful
                || !_clientSession.HasInFlightStreamsAtOrBelow(_clientSession.GoAwayLastStreamId))
            {
                OnConnectionLost(_clientSession.GoAwayLastStreamId);
            }

            return (consumed, data.End);
        }

        if (frameCount > 0)
        {
            ResetKeepAliveTimer();
        }

        return (consumed, data.End);
    }

    private void HandleTransportLost()
    {
        if (_clientSession.HasInFlightRequests)
        {
            OnConnectionLost(_clientSession.GoAwayReceived ? _clientSession.GoAwayLastStreamId : 0);
        }
    }

    private void OnConnectionLost(int lastStreamId)
    {
        Tracing.For("Protocol").Info(this, "HTTP/2: connection lost (lastStreamId={0}, inFlight={1})", lastStreamId, _clientSession.HasInFlightRequests);
        var replayable = ClassifyStreamsForReplay(lastStreamId);
        _reconnect.OnConnectionLost(replayable);

        _clientSession.ReleaseAllStreamState();
        _clientSession.ResetConnectionState();

        _transportOptions ??= OptionsFactory.Build(_clientSession.Endpoint, _options);
        Ops.OnOutbound(new ConnectTransport(_transportOptions));
    }

    private List<HttpRequestMessage> ClassifyStreamsForReplay(int lastStreamId)
    {
        var replayable = new List<HttpRequestMessage>();

        foreach (var (streamId, request) in _clientSession.GetCorrelationMap())
        {
            if (IsStreamSafeToReplay(streamId, request, lastStreamId))
            {
                replayable.Add(request);
            }
            else
            {
                Tracing.For("Protocol").Info(this,
                    "HTTP/2: Dropping non-idempotent or partially-responded request {0} {1} on reconnect",
                    request.Method, request.RequestUri);
                request.Fail(
                    new HttpRequestException("Non-idempotent or partially-responded request dropped on reconnect."));
                request.Dispose();
            }
        }

        return replayable;
    }

    private bool IsStreamSafeToReplay(int streamId, HttpRequestMessage request, int lastStreamId)
    {
        if (lastStreamId > 0 && streamId > lastStreamId)
        {
            return true;
        }

        return IsIdempotentMethod(request.Method) && !_clientSession.HasReceivedHeaders(streamId);
    }

    private static bool IsIdempotentMethod(HttpMethod method)
        => method == HttpMethod.Get
           || method == HttpMethod.Head
           || method == HttpMethod.Options
           || method == HttpMethod.Trace
           || method == HttpMethod.Delete
           || method == HttpMethod.Put;

    private void OnConnectionRestored()
    {
        Tracing.For("Protocol").Info(this, "HTTP/2: connection restored");
        _clientSession.TryEmitPreface();

        _clientSession.FlushPendingInitialRequest();

        if (_pendingRequests is { Count: > 0 } pending)
        {
            _pendingRequests = null;
            foreach (var req in pending)
            {
                _clientSession.EncodeRequest(req);
            }
        }

        var toReplay = _reconnect.OnConnectionRestored();
        for (var i = 0; i < toReplay.Count; i++)
        {
            var req = toReplay[i];

            // A body larger than the buffered-serialization threshold is streamed through the pump
            // from HttpContent.ReadAsStream(), which returns the SAME cached stream now at EOF from
            // the interrupted attempt. Rewind a seekable body so the replay re-sends it in full; fail
            // fast on a consumed forward-only body instead of truncating a fixed-length request.
            if (!RequestBodyReplay.TryRewindOrFail(req, "HTTP/2", this))
            {
                continue;
            }

            _clientSession.EncodeRequest(req);
        }

        ScheduleKeepAlivePing();
    }

    private void OnReconnectAttemptFailed()
    {
        if (!_reconnect.OnReconnectAttemptFailed())
        {
            Tracing.For("Protocol").Info(this, "HTTP/2 reconnect failed after max attempts");
            _reconnect.FailAllBuffered(new HttpRequestException("HTTP/2 reconnect failed after max attempts."));
            return;
        }

        // Defer the retry behind a backoff timer instead of reconnecting immediately, so a
        // connection-refused peer is not hammered in a tight loop (see OnTimerFired).
        if (_options.Http2.ReconnectInitialBackoff > TimeSpan.Zero)
        {
            Ops.OnScheduleTimer(ReconnectBackoff.TimerName, ReconnectBackoff.Compute(
                _reconnect.Attempts - 1,
                _options.Http2.ReconnectInitialBackoff,
                _options.Http2.ReconnectMaxBackoff,
                _options.Http2.ReconnectBackoffMultiplier,
                _options.Http2.ReconnectBackoffJitter,
                Random.Shared));
        }
        else
        {
            Ops.OnOutbound(new ConnectTransport(_transportOptions!));
        }
    }

    private void ScheduleKeepAlivePing()
    {
        if (KeepAliveEnabled)
        {
            Ops.OnScheduleTimer(KeepAlivePingTimerKey, _options.Http2.KeepAlivePingDelay);
        }
    }

    private void ScheduleKeepAlivePingTimeout()
    {
        if (KeepAliveEnabled)
        {
            Ops.OnScheduleTimer(KeepAlivePingTimeoutKey, _options.Http2.KeepAlivePingTimeout);
        }
    }

    private void ResetKeepAliveTimer()
    {
        if (KeepAliveEnabled)
        {
            Ops.OnCancelTimer(KeepAlivePingTimeoutKey);
            ScheduleKeepAlivePing();
        }
    }
}