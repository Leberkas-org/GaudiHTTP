using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Internal;
using GaudiHTTP.Protocol.Multiplexed;
using GaudiHTTP.Streams.Stages.Client;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Syntax.Http2.Client;

internal sealed class Http2ClientStateMachine(
    GaudiClientOptions options,
    IClientStageOperations ops,
    TimeProvider? timeProvider = null)
    : IClientStateMachine
{
    private readonly Http2ClientSessionManager _clientSession = new(options, ops, timeProvider);
    private readonly ReconnectionManager _reconnect = new(options.Http2.MaxReconnectAttempts, options.Http2.MaxReconnectBufferSize);
    private TransportOptions? _transportOptions;

    private const string KeepAlivePingTimerKey = "keep-alive-ping";
    private const string KeepAlivePingTimeoutKey = "keep-alive-ping-timeout";

    private bool KeepAliveEnabled => options.Http2.KeepAlivePingDelay != Timeout.InfiniteTimeSpan;

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
        _clientSession.EncodeRequest(request);
    }

    public void DecodeServerData(ITransportInbound data)
    {
        switch (data)
        {
            case TransportConnected:
                OnConnectionRestored();
                return;

            case TransportDisconnected when _reconnect.IsReconnecting:
                OnReconnectAttemptFailed();
                return;

            case TransportDisconnected when _clientSession.HasInFlightRequests:
                // If we were draining a graceful GOAWAY, classify the still-open streams against that
                // GOAWAY's last-stream-id: streams above it were provably not processed and can be
                // replayed regardless of method, while streams at/below it follow the idempotent rule.
                OnConnectionLost(_clientSession.GoAwayReceived ? _clientSession.GoAwayLastStreamId : 0);
                return;

            case TransportDisconnected:
                return;
        }

        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        int frameCount;
        try
        {
            var frames = _clientSession.DecodeFrames(buffer);
            frameCount = frames.Count;
            for (var i = 0; i < frames.Count; i++)
            {
                _clientSession.ProcessFrame(frames[i]);
            }
        }
        catch (HttpProtocolException ex)
        {
            // RFC 9113 §5.4.1: a connection-fatal protocol error leaves the decoder desynchronized.
            // Drop the connection instead of swallowing and continuing; the resulting TransportDisconnected
            // routes through OnConnectionLost, which replays idempotent in-flight requests and fails the rest.
            Tracing.For("Protocol").Info(this,
                "HTTP/2: connection protocol error - disconnecting: {0}", ex.Message);
            ops.OnOutbound(new DisconnectTransport(DisconnectReason.Error));
            return;
        }

        if (_clientSession is { GoAwayReceived: true, HasInFlightRequests: true })
        {
            // RFC 9113 §6.8: a graceful (NO_ERROR) GOAWAY keeps the connection open until in-progress
            // streams complete. Don't tear it down — let ALL in-flight streams keep draining here
            // (dropping an in-flight non-idempotent POST is exactly the failure seen under load when a
            // server graceful-closes after a batch). New requests already route elsewhere because
            // CanAcceptRequest is now false. Streams the server discarded (above LastStreamId) never get
            // a response and stay in flight until the server closes the connection, at which point the
            // TransportDisconnected path above replays them using the remembered LastStreamId. We only
            // tear the connection down immediately when there is nothing to wait for: a non-graceful
            // (error) GOAWAY, or a graceful GOAWAY whose LastStreamId is below every in-flight stream
            // (the server committed to finish none of them — e.g. LastStreamId=0), in which case
            // draining would just stall until the server closes.
            if (!_clientSession.GoAwayWasGraceful
                || !_clientSession.HasInFlightStreamsAtOrBelow(_clientSession.GoAwayLastStreamId))
            {
                OnConnectionLost(_clientSession.GoAwayLastStreamId);
            }

            return;
        }

        if (frameCount > 0)
        {
            ResetKeepAliveTimer();
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
                    var policy = options.Http2.KeepAlivePingPolicy;
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
                    if (_clientSession.IsKeepAliveTimedOut(options.Http2.KeepAlivePingTimeout))
                    {
                        Tracing.For("Protocol").Info(this, "HTTP/2: Keep-alive PING timeout - closing connection");
                        if (_clientSession.HasInFlightRequests)
                        {
                            OnConnectionLost(lastStreamId: 0);
                        }
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


    public void OnBodyMessage(object msg) => _clientSession.OnBodyMessage(msg);

    public void Cleanup() => _clientSession.Cleanup();

    private void OnConnectionLost(int lastStreamId)
    {
        Tracing.For("Protocol").Info(this, "HTTP/2: connection lost (lastStreamId={0}, inFlight={1})", lastStreamId, _clientSession.HasInFlightRequests);
        var replayable = ClassifyStreamsForReplay(lastStreamId);
        _reconnect.OnConnectionLost(replayable);

        _clientSession.ReleaseAllStreamState();
        _clientSession.ResetConnectionState();

        _transportOptions ??= OptionsFactory.Build(_clientSession.Endpoint, options);
        ops.OnOutbound(new ConnectTransport(_transportOptions));
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
        var preface = _clientSession.TryBuildPreface();
        if (preface is not null)
        {
            ops.OnOutbound(preface);
        }

        var toReplay = _reconnect.OnConnectionRestored();
        for (var i = 0; i < toReplay.Count; i++)
        {
            _clientSession.EncodeRequest(toReplay[i]);
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

        ops.OnOutbound(new ConnectTransport(_transportOptions!));
    }

    private void ScheduleKeepAlivePing()
    {
        if (KeepAliveEnabled)
        {
            ops.OnScheduleTimer(KeepAlivePingTimerKey, options.Http2.KeepAlivePingDelay);
        }
    }

    private void ScheduleKeepAlivePingTimeout()
    {
        if (KeepAliveEnabled)
        {
            ops.OnScheduleTimer(KeepAlivePingTimeoutKey, options.Http2.KeepAlivePingTimeout);
        }
    }

    private void ResetKeepAliveTimer()
    {
        if (KeepAliveEnabled)
        {
            ops.OnCancelTimer(KeepAlivePingTimeoutKey);
            ScheduleKeepAlivePing();
        }
    }
}