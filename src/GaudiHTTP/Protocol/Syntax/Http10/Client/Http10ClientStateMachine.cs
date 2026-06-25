using Akka.Actor;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Internal;
using GaudiHTTP.Pooling;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Streams.Stages.Client;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Syntax.Http10.Client;

internal sealed class Http10ClientStateMachine : IClientStateMachine, IBodyDrainTarget
{
    private readonly IClientStageOperations _ops;
    private readonly Http10ClientDecoder _decoder;
    private readonly Http10ClientEncoder _encoder;
    private readonly GaudiClientOptions _options;
    private readonly ConnectionPoolContext _poolContext = new();
    private TransportOptions? _transportOptions;
    private HttpRequestMessage? _inFlightRequest;
    private HttpRequestMessage? _reconnectBufferedRequest;
    private int _reconnectAttempts;
    private bool _lastRequestWasHead;
    private bool _outboundBodyPending;
    private HttpRequestMessage? _deferredRequest;
    private IStreamingBodyReader? _activeStreamingReader;
    private bool _connectionClosed;
    private SerialBodyPump? _serialPump;
    private CancellationTokenSource? _connectionCts;

    internal sealed record StreamingSlotFreed;

    public bool CanAcceptRequest => _inFlightRequest is null && !IsReconnecting && !_outboundBodyPending;

    public bool HasInFlightRequests => _inFlightRequest is not null;

    public bool IsReconnecting { get; private set; }

    public bool ShouldPauseNetwork => _activeStreamingReader?.IsFull ?? false;

    private int PendingRequestCount
    {
        get
        {
            if (IsReconnecting)
            {
                return _reconnectBufferedRequest is not null ? 1 : 0;
            }

            return _inFlightRequest is not null ? 1 : 0;
        }
    }

    public RequestEndpoint Endpoint { get; private set; }

    public Http10ClientStateMachine(IClientStageOperations ops, GaudiClientOptions options)
    {
        _ops = ops;
        _options = options;

        var decoderOpts = options.ToHttp10DecoderOptions();

        _decoder = new Http10ClientDecoder(decoderOpts, _poolContext);
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
            var item = TransportBuffer.Rent(data.Length);
            data.CopyTo(item.FullMemory);
            item.Length = data.Length;
            _ops.OnOutbound(TransportData.Rent(item));
            Tracing.For("Protocol").Trace(this, "HTTP/1.0 request body chunk flushed (bytes={0})", data.Length);

            // H1.0 has no OnOutboundFlushed — drive the pump inline after each chunk.
            _serialPump!.OnCapacityAvailable();
        }

        if (endStream)
        {
            _outboundBodyPending = false;
            _inFlightRequest = _deferredRequest;
            _deferredRequest = null;
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
        if (_deferredRequest is not null)
        {
            _deferredRequest.Fail(new HttpRequestException("Failed to read HTTP/1.0 request body.", reason));
            _deferredRequest = null;
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
            if (_reconnectBufferedRequest is { } buffered)
            {
                buffered.Fail(new HttpRequestException("HTTP/1.0 transport closed during reconnect."));
                _reconnectBufferedRequest = null;
            }

            IsReconnecting = false;
            _reconnectAttempts = 0;
            Tracing.For("Protocol").Debug(this, "HTTP/1.0 transport closed during reconnect");
            return;
        }

        TryDecodeEof(bodyComplete);
        FailOrphanedRequest();
    }

    public void OnTimerFired(string name)
    {
    }

    public void OnBodyMessage(object msg)
    {
        switch (msg)
        {
            case StreamingSlotFreed:
                break;

            case DrainReadComplete read:
                _serialPump?.HandleReadComplete(read.BytesRead);
                break;

            case DrainReadFailed failed:
                _serialPump?.HandleReadFailed(failed.Reason);
                break;

            case DrainContinue:
                _serialPump?.HandleDrainContinue();
                break;
        }
    }

    public void Cleanup()
    {
        _inFlightRequest = null;
        _outboundBodyPending = false;
        _activeStreamingReader = null;
        _deferredRequest = null;
        _connectionClosed = false;
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

        TransportBuffer? item = null;
        try
        {
            // Capture Content-Length BEFORE Encode(), because ReadAsStream() internally
            // buffers the content and may cause ContentLength to become non-null afterwards.
            var knownCl = request.Content?.Headers.ContentLength;
            var contentLength = Convert.ToInt32(knownCl ?? 0);
            item = TransportBuffer.Rent(HttpMessageSize.Estimate(request, contentLength));
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
                _deferredRequest = request;
                _inFlightRequest = null;
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
        _serialPump = new SerialBodyPump(this, EnsureConnectionCts(), 16 * 1024, maxCapacity: 2);
        _serialPump.Register(bodyStream, contentLength: null, CancellationToken.None);
    }

    private void DecodeResponse(TransportBuffer buffer)
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

        if (HasInFlightRequests && _options.Http1.MaxReconnectAttempts > 0)
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

    private void TryDecodeEof(bool bodyComplete)
    {
        TryCompleteAfterEof(bodyComplete);
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
        _reconnectBufferedRequest = _inFlightRequest;
        _inFlightRequest = null;
        IsReconnecting = true;
        _reconnectAttempts = 1;
        _ops.OnOutbound(new ConnectTransport(_transportOptions!));
    }

    private void OnConnectionRestored()
    {
        IsReconnecting = false;
        _reconnectAttempts = 0;
        _connectionClosed = false;
        _decoder.Reset();

        if (_reconnectBufferedRequest is { } req)
        {
            _reconnectBufferedRequest = null;
            EncodeRequest(req);
        }
    }

    private void OnReconnectAttemptFailed()
    {
        if (_reconnectAttempts >= _options.Http1.MaxReconnectAttempts)
        {
            Tracing.For("Protocol").Info(this, "HTTP/1.0 reconnect failed after {0} attempts", _reconnectAttempts);
            if (_reconnectBufferedRequest is { } buffered)
            {
                buffered.Fail(new HttpRequestException("HTTP/1.0 reconnect failed after max attempts."));
                _reconnectBufferedRequest = null;
            }

            IsReconnecting = false;
            _reconnectAttempts = 0;
            return;
        }

        _reconnectAttempts++;
        _ops.OnOutbound(new ConnectTransport(_transportOptions!));
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
