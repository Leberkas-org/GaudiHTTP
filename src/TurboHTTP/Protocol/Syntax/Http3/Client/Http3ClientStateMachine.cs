using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Multiplexed;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Streams.Stages.Client;
using static Servus.Senf;

namespace TurboHTTP.Protocol.Syntax.Http3.Client;

internal sealed class Http3ClientStateMachine : IClientStateMachine
{
    private const string IdleTimeoutCheckTimer = "idle-timeout-check";
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly TurboClientOptions _options;
    private readonly IClientStageOperations _ops;
    private TransportOptions? _transportOptions;

    private readonly Http3ClientSessionManager _clientSession;
    private readonly ReconnectionManager _reconnect;

    private readonly Server.ServerStreamResolver _serverStreamResolver;

    // QUIC reports a connection failure as StreamClosed(Error) per stream FOLLOWED by a
    // TransportDisconnected. When the per-stream errors already drove the reconnect, the trailing
    // TransportDisconnected belongs to the SAME failure and must be swallowed once — not counted as a
    // failed reconnect attempt. Set when a stream-error starts the reconnect; consumed by the next
    // TransportDisconnected and cleared on a successful reconnect.
    private bool _expectTrailingDisconnect;

    public bool CanAcceptRequest => !Connection.GoAwayReceived && !IsReconnecting && _clientSession.CanOpenStream;

    public bool IsReconnecting => _reconnect.IsReconnecting;

    public int ReconnectBufferCount => _reconnect.BufferedCount;

    public bool HasInFlightRequests => _clientSession.HasInFlightRequests;

    public RequestEndpoint Endpoint => _clientSession.Endpoint;

    private ConnectionState Connection { get; }

    public Http3ClientStateMachine(TurboClientOptions options, IClientStageOperations ops)
    {
        _options = options;
        _ops = ops;

        var encoderOpts = options.ToHttp3EncoderOptions();
        var decoderOpts = options.ToHttp3DecoderOptions();

        _clientSession = new Http3ClientSessionManager(encoderOpts, decoderOpts, options, ops);
        _reconnect = new ReconnectionManager(options.Http3.MaxReconnectAttempts, options.Http3.MaxReconnectBufferSize);
        _serverStreamResolver = new Server.ServerStreamResolver
        {
            OnPushStreamDetected = HandleIncomingPushStream
        };

        var idleTimeout = options.Http3.IdleTimeout == TimeSpan.Zero
            ? DefaultIdleTimeout
            : options.Http3.IdleTimeout;

        Connection = new ConnectionState(idleTimeout);
    }

    public void PreStart()
    {
        _clientSession.OpenCriticalStreams();
        ScheduleIdleCheck();
    }

    public void OnRequest(HttpRequestMessage request)
    {
        if (Connection.GoAwayReceived)
        {
            Tracing.For("Protocol").Warning(this, "RFC 9114 §5.2 - GOAWAY received; dropping outbound request.");
            return;
        }

        if (IsReconnecting)
        {
            BufferForReconnect(request);
            return;
        }

        _clientSession.EncodeRequest(request);
    }

    public void DecodeServerData(ITransportInbound data)
    {
        switch (data)
        {
            case TransportConnected:
                {
                    _clientSession.OnTransportConnected();
                    OnConnectionRestored();
                    return;
                }

            case TransportDisconnected when IsReconnecting:
                {
                    // A trailing disconnect from a stream-error-driven failure (StreamClosed(Error) per
                    // stream + a final TransportDisconnected) is the same failure, not a failed reconnect
                    // attempt — swallow it once. A later disconnect IS the new connect attempt failing.
                    if (_expectTrailingDisconnect)
                    {
                        _expectTrailingDisconnect = false;
                        return;
                    }

                    OnReconnectAttemptFailed();
                    return;
                }

            case TransportDisconnected when HasInFlightRequests:
                {
                    OnConnectionLost(expectTrailingDisconnect: false);
                    return;
                }

            case TransportDisconnected:
                {
                    _clientSession.OnTransportDisconnected();
                    return;
                }

            case ServerStreamAccepted { Id: var id }:
                {
                    _serverStreamResolver.OnServerStreamOpened(id);
                    return;
                }

            case StreamOpened:
                {
                    return;
                }

            case StreamReadCompleted { Id.Value: >= 0 } readCompleted:
                {
                    _clientSession.FlushPendingResponse(readCompleted.Id.Value);
                    return;
                }

            case StreamReadCompleted:
                {
                    return;
                }

            case StreamClosed { Id.Value: >= 0 } streamClosed:
                {
                    Connection.OnStreamClosed();
                    if (streamClosed.Reason == DisconnectReason.Error)
                    {
                        OnConnectionLost(expectTrailingDisconnect: true);
                    }
                    else
                    {
                        _clientSession.FlushPendingResponse(streamClosed.Id.Value);
                    }

                    return;
                }

            case StreamClosed:
                {
                    _clientSession.FlushAllPendingResponses();
                    return;
                }

            case MultiplexedData multiplexed:
                {
                    HandleTaggedStreamData(multiplexed);
                    return;
                }

            case TransportData rawData:
                {
                    Tracing.For("Protocol").Warning(this,
                        "Received untagged TransportData - dropping to prevent stream ID misrouting.");
                    rawData.Buffer.Dispose();
                    return;
                }
        }
    }

    public void OnUpstreamFinished()
    {
        _clientSession.FlushAllPendingResponses();

        if (IsReconnecting)
        {
            Tracing.For("Protocol").Debug(this,
                "HTTP/3 transport closed during reconnect - discarding in-flight request(s).");
            var correlations = _clientSession.SnapshotAndClearCorrelations();
            if (correlations.Count > 0)
            {
                RequestFault.FailAll(correlations,
                    new HttpRequestException("HTTP/3 transport closed during reconnect."));
            }
        }
    }

    public void OnTimerFired(string name)
    {
        if (name != IdleTimeoutCheckTimer)
        {
            return;
        }

        var goAway = CheckIdleTimeout();
        if (goAway is not null)
        {
            var buf = TransportBuffer.Rent(goAway.SerializedSize);
            var span = buf.FullMemory.Span;
            goAway.WriteTo(ref span);
            buf.Length = goAway.SerializedSize;
            _ops.OnOutbound(new MultiplexedData(buf, CriticalStreamId.Control));
            return;
        }

        ScheduleIdleCheck();
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
            Tracing.For("Protocol").Debug(this, "HTTP/3: cancelled request, sent STOP_SENDING");
        }
    }

    public void OnBodyMessage(object msg)
    {
        _clientSession.OnBodyMessage(msg);
    }

    public void Cleanup()
    {
        _clientSession.Cleanup();
    }


    private Http3Frame? ProcessFrame(Http3Frame frame)
    {
        Connection.RecordActivity();

        switch (frame)
        {
            case SettingsFrame settings:
                HandleSettings(settings);
                return null;

            case GoAwayFrame goAway:
                HandleGoAway(goAway);
                return null;

            case PushPromiseFrame pushPromise:
                return HandlePushPromise(pushPromise);

            case CancelPushFrame cancelPush:
                Connection.OnReceivedCancelPush(cancelPush);
                return null;

            case MaxPushIdFrame:
                return null;

            case HeadersFrame:
            default:
                return frame;
        }
    }

    private GoAwayFrame? CheckIdleTimeout()
    {
        if (!Connection.IsIdleTimeoutExpired() || Connection.ActiveStreamCount != 0) return null;
        Tracing.For("Protocol").Info(this,
            "RFC 9114 §5.1 - idle timeout expired with no active streams; sending GOAWAY.");
        return new GoAwayFrame(0);
    }

    private void OnConnectionLost(bool expectTrailingDisconnect)
    {
        // Idempotent: QUIC surfaces one connection failure as a StreamClosed(Error) PER stream (plus a
        // trailing TransportDisconnected), so this can fire several times for a single failure. Only the
        // first call may capture the in-flight requests and start the reconnect. A second call would
        // re-buffer an ALREADY-DRAINED (empty) correlation map via ReconnectionManager.OnConnectionLost,
        // wiping the replay set (losing those requests) and emitting a duplicate ConnectTransport. This
        // mirrors TCP, where the transport reports a single disconnect and the state machine owns reconnect.
        if (IsReconnecting)
        {
            return;
        }

        _expectTrailingDisconnect = expectTrailingDisconnect;

        Tracing.For("Protocol").Info(this, "HTTP/3: connection lost (inFlight={0})", HasInFlightRequests);
        var correlations = _clientSession.GetCorrelationMap().Values.ToList();
        _reconnect.OnConnectionLost(correlations);

        _clientSession.DrainStreams();
        _clientSession.ResetConnectionState();

        Connection.Reset();
        _serverStreamResolver.Reset();

        _transportOptions ??= OptionsFactory.Build(Endpoint, _options);
        _ops.OnOutbound(new ConnectTransport(_transportOptions));
    }

    private void OnConnectionRestored()
    {
        _expectTrailingDisconnect = false;
        Tracing.For("Protocol").Info(this, "HTTP/3: connection restored");
        var preface = _clientSession.TryBuildControlPreface();
        if (preface is not null)
        {
            _ops.OnOutbound(preface);
        }

        var toReplay = _reconnect.OnConnectionRestored();
        for (var i = 0; i < toReplay.Count; i++)
        {
            _clientSession.EncodeRequest(toReplay[i]);
        }
    }

    private void OnReconnectAttemptFailed()
    {
        if (!_reconnect.OnReconnectAttemptFailed())
        {
            Tracing.For("Protocol").Info(this, "HTTP/3 reconnect failed after max attempts");
            _reconnect.FailAllBuffered(new HttpRequestException("HTTP/3 reconnect failed after max attempts."));
            return;
        }

        _ops.OnOutbound(new ConnectTransport(_transportOptions!));
    }

    private void ScheduleIdleCheck()
    {
        if (Connection.IsTimeoutDisabled)
        {
            return;
        }

        var remaining = Connection.TimeUntilExpiry();
        var checkInterval = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
        _ops.OnScheduleTimer(IdleTimeoutCheckTimer, checkInterval);
    }

    private void BufferForReconnect(HttpRequestMessage request)
    {
        if (!_reconnect.Buffer(request))
        {
            request.Fail(new HttpRequestException("HTTP/3 reconnect buffer full."));
        }
    }


    private void HandleSettings(SettingsFrame settings)
    {
        try
        {
            Connection.OnRemoteSettings(settings);
            Tracing.For("Protocol").Info(this, "RFC 9114 §7.2.4 - remote SETTINGS received ({0} parameters).",
                settings.Parameters.Count);

            _clientSession.HandleSettings(settings);
        }
        catch (HttpProtocolException ex)
        {
            // RFC 9114 §7.2.4: a malformed or repeated SETTINGS frame is a connection error (H3_SETTINGS_ERROR).
            DisconnectOnConnectionError("control SETTINGS", ex);
        }
    }

    private void HandleGoAway(GoAwayFrame goAway)
    {
        try
        {
            Connection.OnServerGoAway(goAway);
            Tracing.For("Protocol").Info(this, "RFC 9114 §5.2 - GOAWAY received (streamId={0}).", goAway.StreamId);
        }
        catch (HttpProtocolException ex)
        {
            // RFC 9114 §5.2: a GOAWAY with an invalid or increasing stream ID is a connection error.
            DisconnectOnConnectionError("control GOAWAY", ex);
        }
    }

    private PushPromiseFrame? HandlePushPromise(PushPromiseFrame pushPromise)
    {
        var cancelFrame = new CancelPushFrame(pushPromise.PushId);
        var buf = TransportBuffer.Rent(cancelFrame.SerializedSize);
        var span = buf.FullMemory.Span;
        cancelFrame.WriteTo(ref span);
        buf.Length = cancelFrame.SerializedSize;
        _ops.OnOutbound(new MultiplexedData(buf, CriticalStreamId.Control));
        Tracing.For("Protocol").Info(this,
            "RFC 9114 §7.2.5 - push promise rejected (pushId={0}); server push not supported", pushPromise.PushId);
        return null;
    }

    private void HandleIncomingPushStream(long quicStreamId, ReadOnlySpan<byte> remaining)
    {
        long pushId = -1;
        if (QuicVarInt.TryDecode(remaining, out var id, out _))
        {
            pushId = id;
        }

        if (pushId >= 0)
        {
            var cancel = new CancelPushFrame(pushId);
            var buf = TransportBuffer.Rent(cancel.SerializedSize);
            var span = buf.FullMemory.Span;
            cancel.WriteTo(ref span);
            buf.Length = cancel.SerializedSize;
            _ops.OnOutbound(new MultiplexedData(buf, CriticalStreamId.Control));
        }

        _ops.OnOutbound(new ResetStream(quicStreamId));
        Tracing.For("Protocol").Info(this,
            "RFC 9114 §4.6 - push stream {0} (pushId={1}) reset (push response delivery not implemented)", quicStreamId,
            pushId);
    }

    /// <summary>
    /// RFC 9114 §8 / RFC 9204 §2.2: a connection-fatal H3/QPACK error leaves the decoder or dynamic table
    /// desynchronized. Disconnect the transport rather than swallowing and continuing; the resulting
    /// TransportDisconnected routes through OnConnectionLost, which replays idempotent in-flight requests.
    /// </summary>
    private void DisconnectOnConnectionError(string context, Exception ex)
    {
        Tracing.For("Protocol").Info(this,
            "HTTP/3: connection-fatal error ({0}) - disconnecting: {1}", context, ex.Message);
        _ops.OnOutbound(new DisconnectTransport(DisconnectReason.Error));
    }

    private void HandleTaggedStreamData(MultiplexedData multiplexed)
    {
        var resolved = _serverStreamResolver.Resolve(multiplexed.StreamId, multiplexed.Buffer);

        if (resolved.Buffer is null)
        {
            return;
        }

        switch (resolved.LogicalStreamId)
        {
            case CriticalStreamId.QpackDecoderId:
                {
                    try
                    {
                        _clientSession.ProcessQpackDecoderBytes(resolved.Buffer.Memory);
                    }
                    catch (Exception ex) when (ex is QpackException or HuffmanException)
                    {
                        DisconnectOnConnectionError("QPACK decoder stream", ex);
                    }
                    finally
                    {
                        resolved.Buffer.Dispose();
                    }

                    return;
                }
            case CriticalStreamId.QpackEncoderId:
                {
                    try
                    {
                        _clientSession.ProcessQpackEncoderBytes(resolved.Buffer.Memory);
                    }
                    catch (Exception ex) when (ex is QpackException or HuffmanException)
                    {
                        DisconnectOnConnectionError("QPACK encoder stream", ex);
                    }
                    finally
                    {
                        resolved.Buffer.Dispose();
                    }

                    return;
                }
            case CriticalStreamId.ControlId:
                {
                    ProcessFrameData(resolved.Buffer, CriticalStreamId.ControlId);
                    return;
                }
            default:
                {
                    ProcessFrameData(resolved.Buffer, resolved.LogicalStreamId);
                    return;
                }
        }
    }

    private void ProcessFrameData(TransportBuffer buffer, long streamId)
    {
        // Decoded frames may slice the input buffer (zero-copy), so it must stay alive
        // until the frame loop below has handled (and copied) everything.
        using var inputBuffer = buffer;
        try
        {
            var frames = _clientSession.DecodeServerData(inputBuffer, streamId);

            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                try
                {
                    var forwarded = ProcessFrame(frame);
                    if (forwarded is not null)
                    {
                        _clientSession.AssembleResponse(forwarded, streamId);
                    }
                }
                finally
                {
                    // DATA/HEADERS frames own a pooled rental; response assembly copies what
                    // it keeps (body bytes into the body reader, header strings via QPACK),
                    // so the rental must go back to the pool here.
                    (frame as IDisposable)?.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is HttpProtocolException or QpackException or HuffmanException)
        {
            // RFC 9114 §8: a framing or header-decode failure leaves the decoder/dynamic table
            // desynchronized for the whole connection. Disconnect instead of letting it escape the
            // decode loop (where the stage would swallow it and continue against corrupt state).
            DisconnectOnConnectionError("frame decode", ex);
        }
    }
}