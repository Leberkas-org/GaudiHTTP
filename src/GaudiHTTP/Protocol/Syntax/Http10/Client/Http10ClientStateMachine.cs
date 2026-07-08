using System.Buffers;
using Akka.Actor;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Internal;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Streams.Stages.Client;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Syntax.Http10.Client;

internal sealed class Http10ClientStateMachine : IClientStateMachine, IBodyDrainTarget
{
    private readonly IClientStageOperations _ops;
    private readonly Http10ClientDecoder _decoder;
    private readonly Http10ClientEncoder _encoder;
    private readonly GaudiClientOptions _options;
    private TransportOptions? _transportOptions;
    private HttpRequestMessage? _inFlightRequest;
    private readonly ReconnectPolicy<HttpRequestMessage> _reconnectPolicy;
    private bool _lastRequestWasHead;
    private bool _outboundBodyPending;
    private IStreamingBodyReader? _activeStreamingReader;
    private bool _connectionClosed;
    private bool _connectionDead;
    private SerialBodyPump? _serialPump;
    private CancellationTokenSource? _connectionCts;

    internal sealed record StreamingSlotFreed;

    public bool CanAcceptRequest => _inFlightRequest is null && !IsReconnecting && !_outboundBodyPending && !_connectionDead;

    public bool HasInFlightRequests => _inFlightRequest is not null || _outboundBodyPending;

    public bool IsReconnecting { get; private set; }

    public bool ShouldPauseNetwork => _activeStreamingReader?.IsFull ?? false;

    private int PendingRequestCount
    {
        get
        {
            if (IsReconnecting)
            {
                return _reconnectPolicy.Buffered is not null ? 1 : 0;
            }

            return (_inFlightRequest is not null || _outboundBodyPending) ? 1 : 0;
        }
    }

    public RequestEndpoint Endpoint { get; private set; }

    public Http10ClientStateMachine(GaudiClientOptions options, IClientStageOperations ops)
    {
        _ops = ops;
        _options = options;
        _reconnectPolicy = new ReconnectPolicy<HttpRequestMessage>(
            ops,
            options.Http1.MaxReconnectAttempts,
            options.Http1.ReconnectInitialBackoff,
            options.Http1.ReconnectMaxBackoff,
            options.Http1.ReconnectBackoffMultiplier,
            options.Http1.ReconnectBackoffJitter);

        var decoderOpts = options.ToHttp10DecoderOptions();

        _decoder = new Http10ClientDecoder(decoderOpts);
        _encoder = new Http10ClientEncoder();
    }

    public void PreStart()
    {
    }

    private CancellationTokenSource EnsureConnectionCts()
    {
        return _connectionCts ??= new CancellationTokenSource();
    }

    IActorRef IBodyDrainTarget.StageActor => _ops.StageActor;

    void IBodyDrainTarget.EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
    {
        if (!data.IsEmpty)
        {
            var item = WireBuffer.Rent(data.Length);
            data.CopyTo(item.FullMemory);
            item.Length = data.Length;
            _ops.OnOutbound(TransportData.Rent(item));
            Tracing.For("Protocol").Trace(this, "HTTP/1.0 request body chunk flushed (bytes={0})", data.Length);

            // H1.0 has no real-flush routing — refill the pump's byte budget inline after each chunk
            // so the drain keeps flowing (behavior unchanged; still watermark-guarded on the TCP side).
            _serialPump?.ResetCredit();
        }

        if (endStream)
        {
            _outboundBodyPending = false;
            Tracing.For("Protocol").Debug(this, "HTTP/1.0 request body complete (pump)");
        }
    }

    void IBodyDrainTarget.EmitOwnedDataFrames(int streamId, IMemoryOwner<byte> owner, int bytesWritten, bool endStream)
    {
        if (bytesWritten > 0)
        {
            _ops.OnOutbound(TransportData.Rent(WireBuffer.Wrap(owner, 0, bytesWritten)));
            Tracing.For("Protocol").Trace(this, "HTTP/1.0 request body chunk flushed (bytes={0})", bytesWritten);
            _serialPump?.ResetCredit();
        }
        else
        {
            owner.Dispose();
        }

        if (endStream)
        {
            _outboundBodyPending = false;
            Tracing.For("Protocol").Debug(this, "HTTP/1.0 request body complete (pump)");
        }
    }

    void IBodyDrainTarget.OnDrainComplete(int streamId)
    {
        Tracing.For("Protocol").Debug(this, "HTTP/1.0 request body drain complete");
    }

    void IBodyDrainTarget.OnDrainFailed(int streamId, Exception reason)
    {
        Tracing.For("Protocol").Warning(this, "request body failed: {0}", reason.Message);
        _outboundBodyPending = false;
        if (_inFlightRequest is not null)
        {
            _inFlightRequest.Fail(new HttpRequestException("Failed to read HTTP/1.0 request body.", reason));
            _inFlightRequest = null;
        }
    }

    public void OnRequest(HttpRequestMessage request)
    {
        EncodeRequest(request);
    }

    public void OnRequestCancelled(HttpRequestMessage request)
    {
        if (_inFlightRequest is not null && ReferenceEquals(_inFlightRequest, request))
        {
            request.Fail(new OperationCanceledException("Request cancelled by caller."));
            _inFlightRequest = null;
            _ops.OnOutbound(new DisconnectTransport(DisconnectReason.Error));
            Tracing.For("Protocol").Debug(this, "HTTP/1.0: cancelled request, disconnecting");
        }
    }

    public void DecodeServerData(ITransportInbound data)
    {
        switch (data)
        {
            case TransportConnected:
                OnConnectionRestored();
                return;

            case TransportDisconnected when IsReconnecting:
                OnReconnectAttemptFailed();
                return;

            case TransportDisconnected disconnect when !IsReconnecting:
                HandleDisconnect(disconnect);
                return;
        }

        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        DecodeResponse(buffer);
    }

    public void OnUpstreamFinished()
    {
        var bodyComplete = _decoder.SignalEof();

        if (IsReconnecting)
        {
            if (_reconnectPolicy.TakeBuffered() is { } buffered)
            {
                buffered.Fail(new HttpRequestException("HTTP/1.0 transport closed during reconnect."));
            }

            IsReconnecting = false;
            Tracing.For("Protocol").Debug(this, "HTTP/1.0 transport closed during reconnect");
            return;
        }

        TryCompleteAfterEof(bodyComplete);
        FailOrphanedRequest();
    }

    public void OnTimerFired(string name)
    {
        _reconnectPolicy.OnReconnectTimerFired(name);
    }

    public void OnBodyMessage(object msg)
    {
        switch (msg)
        {
            case StreamingSlotFreed:
                break;

            case BodyReadComplete<int> read:
                _serialPump?.HandleReadComplete(read.BytesRead);
                break;

            case BodyReadFailed<int> failed:
                _serialPump?.HandleReadFailed(failed.Reason);
                break;
        }
    }

    public void Cleanup()
    {
        _inFlightRequest = null;
        _outboundBodyPending = false;
        _activeStreamingReader = null;
        _connectionClosed = false;
        _connectionDead = false;
        _serialPump?.Cleanup();
        _serialPump = null;
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _connectionCts = null;
        _decoder.Reset();
    }

    private void EncodeRequest(HttpRequestMessage request)
    {
        _inFlightRequest = request;
        _lastRequestWasHead = request.Method == HttpMethod.Head;

        var endpoint = RequestEndpoint.FromRequest(request);

        if (Endpoint == default && endpoint != default)
        {
            Endpoint = endpoint;
            _transportOptions = OptionsFactory.Build(endpoint, _options);
            _ops.OnOutbound(new ConnectTransport(_transportOptions));
        }
        else if (_connectionClosed && _transportOptions is not null)
        {
            _connectionClosed = false;
            _ops.OnOutbound(new ConnectTransport(_transportOptions));
        }

        WireBuffer? item = null;
        try
        {
            // Capture Content-Length BEFORE Encode(), because ReadAsStream() internally
            // buffers the content and may cause ContentLength to become non-null afterwards.
            var knownCl = request.Content?.Headers.ContentLength;
            var contentLength = Convert.ToInt32(knownCl ?? 0);
            item = WireBuffer.Rent(HttpMessageSize.Estimate(request, contentLength));
            var span = item.FullMemory.Span;

            var written = _encoder.Encode(span, request, out var bodyStream);
            if (written > 0)
            {
                item.Length = written;
                _ops.OnOutbound(TransportData.Rent(item));
            }
            else if (bodyStream is not null)
            {
                if (!knownCl.HasValue)
                {
                    throw new InvalidOperationException(
                        "HTTP/1.0 requires a known Content-Length for request bodies. " +
                        "Use an HttpContent implementation that supports TryComputeLength.");
                }

                // Known Content-Length: emit headers now, stream body via SerialBodyPump.
                var headerWritten = _encoder.EncodeHeadersOnly(span, request, knownCl.Value);
                item.Length = headerWritten;
                _ops.OnOutbound(TransportData.Rent(item));
                // Correlate from headers-dispatch onward: with force-async the body drains via the
                // mailbox, so a response may arrive before the body completes. Keeping the request
                // in-flight (rather than deferring _inFlightRequest until endStream) lets it correlate.
                _inFlightRequest = request;
                _outboundBodyPending = true;
                StartBodyDrain(bodyStream);
            }
            else
            {
                item.Dispose();
                item = null;
            }
        }
        catch (Exception ex)
        {
            item?.Dispose();
            Tracing.For("Protocol").Error(this, "Failed to encode HTTP/1.0 request [{0}]: {1}", request.RequestUri,
                ex.Message);
            request.Fail(ex);
            _inFlightRequest = null;
        }
    }

    private void StartBodyDrain(Stream bodyStream)
    {
        _serialPump = new SerialBodyPump(this, EnsureConnectionCts(),
            _options.ResolveRequestBodyChunkSize(_options.Http1), maxBytes: 256 * 1024);
        _serialPump.Register(bodyStream, contentLength: null, CancellationToken.None);
    }

    private void DecodeResponse(WireBuffer buffer)
    {
        try
        {
            var outcome = _decoder.Feed(buffer.Memory, _lastRequestWasHead, out _);

            if (outcome == DecodeOutcome.Complete)
            {
                var response = _decoder.GetResponse();
                CompleteResponse(response);
                _decoder.Reset();
            }
            else if (_decoder.IsBodyStreaming)
            {
                var response = _decoder.GetResponse();
                if (_inFlightRequest is not null)
                {
                    response.RequestMessage = _inFlightRequest;
                }

                _ops.OnResponse(response);

                if (_decoder.StreamingReader is { } sr)
                {
                    _activeStreamingReader = sr;
                    sr.SlotFreed += () =>
                        _ops.StageActor.Tell(new StreamingSlotFreed(), ActorRefs.NoSender);
                }
            }
        }
        catch (Exception ex)
        {
            Tracing.For("Protocol").Error(this, "Failed to decode HTTP/1.0 response: {0}", ex.Message);
            if (_inFlightRequest is { } req)
            {
                req.Fail(new HttpRequestException("Failed to decode HTTP/1.0 response.", ex));
                _inFlightRequest = null;
            }

            _activeStreamingReader = null;
            _decoder.Reset();
        }
        finally
        {
            buffer.Dispose();
        }
    }

    private void HandleDisconnect(TransportDisconnected disconnect)
    {
        var isGraceful = disconnect.Reason == DisconnectReason.Graceful;

        var bodyComplete = _decoder.SignalEof();
        _connectionClosed = true;

        if (isGraceful)
        {
            TryCompleteAfterEof(bodyComplete);
            return;
        }

        if (HasInFlightRequests && _reconnectPolicy.CanReconnect)
        {
            Tracing.For("Protocol").Info(this, "HTTP/1.0 closed, {0} pending — reconnecting", PendingRequestCount);
            StartReconnect();
            return;
        }

        const string message = "Connection was aborted while receiving HTTP/1.0 response.";

        if (_inFlightRequest is { } req)
        {
            req.Fail(new HttpRequestException(message));
            _inFlightRequest = null;
        }

        _decoder.Reset();
        Tracing.For("Protocol").Info(this, "HTTP/1.0: {0}", message);
    }

    private void TryCompleteAfterEof(bool bodyComplete)
    {
        if (_activeStreamingReader is not null)
        {
            _activeStreamingReader = null;
            _inFlightRequest = null;
            _decoder.Reset();
            return;
        }

        if (_inFlightRequest is null)
        {
            _decoder.Reset();
            return;
        }

        if (!bodyComplete)
        {
            Tracing.For("Protocol").Error(this, "HTTP/1.0 connection closed before response body was complete");
            _inFlightRequest.Fail(
                new HttpRequestException("HTTP/1.0 connection closed before response body was complete."));
            _inFlightRequest = null;
            _decoder.Reset();
            return;
        }

        try
        {
            var response = _decoder.GetResponse();
            _decoder.Reset();
            CompleteResponse(response);
        }
        catch (Exception ex)
        {
            Tracing.For("Protocol").Error(this, "Failed to complete HTTP/1.0 response at EOF: {0}", ex.Message);
            _inFlightRequest.Fail(new HttpRequestException("Failed to complete HTTP/1.0 response at EOF.", ex));
            _inFlightRequest = null;
            _decoder.Reset();
        }
    }

    private void FailOrphanedRequest()
    {
        if (_inFlightRequest is not null)
        {
            Tracing.For("Protocol").Error(this, "HTTP/1.0 connection closed with orphaned request — failing");
            _inFlightRequest.Fail(new HttpRequestException("HTTP/1.0 connection closed with orphaned request."));
            _inFlightRequest = null;
        }
    }

    private void StartReconnect()
    {
        var buffered = _inFlightRequest;
        _inFlightRequest = null;
        IsReconnecting = true;
        _reconnectPolicy.Start(buffered!, _transportOptions!);
    }

    private void OnConnectionRestored()
    {
        IsReconnecting = false;
        _connectionClosed = false;
        _decoder.Reset();

        if (_reconnectPolicy.TakeBuffered() is { } req)
        {
            EncodeRequest(req);
        }
    }

    private void OnReconnectAttemptFailed()
    {
        var attemptsAtFailure = _reconnectPolicy.Attempts;

        if (_reconnectPolicy.OnAttemptFailed(_transportOptions!, out var buffered))
        {
            Tracing.For("Protocol").Info(this, "HTTP/1.0 reconnect failed after {0} attempts", attemptsAtFailure);
            buffered?.Fail(new HttpRequestException("HTTP/1.0 reconnect failed after max attempts."));

            IsReconnecting = false;
            _connectionDead = true;
        }
    }

    private void CompleteResponse(HttpResponseMessage response)
    {
        var request = _inFlightRequest;
        _inFlightRequest = null;
        _connectionClosed = true;

        if (request is not null)
        {
            response.RequestMessage = request;
        }

        _ops.OnResponse(response);
    }
}
