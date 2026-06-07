using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Akka.Actor;
using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Streams.Stages.Client;
using static Servus.Senf;

namespace TurboHTTP.Protocol.Syntax.Http11.Client;

internal sealed class Http11ClientStateMachine : IClientStateMachine
{
    private readonly IClientStageOperations _ops;
    private readonly Http11ClientDecoder _decoder;
    private readonly Http11ClientEncoder _encoder;
    private readonly TurboClientOptions _options;

    private readonly Queue<HttpRequestMessage> _inFlightQueue = new();
    private Queue<HttpRequestMessage>? _reconnectBufferedQueue;
    private readonly int _effectivePipelineDepth;
    private int _reconnectAttempts;
    private TransportOptions? _transportOptions;
    private HttpResponseMessage? _pendingBodyResponse;
    private bool _outboundBodyPending;
    private bool _connectionCloseReceived;
    private readonly ConnectionBodyPool _pool = new();
    private IBodyWriter? _currentWriter;
    private Stream? _currentBodyStream;
    private IStreamingBodyReader? _activeStreamingReader;
    private TransportBuffer? _heldBuffer;
    private int _heldBufferOffset;
    private bool _draining;

    internal sealed record BodyReadComplete(int BytesRead);
    internal sealed record BodyReadFailed(Exception Reason);
    internal sealed record StreamingSlotFreed;

    public bool CanAcceptRequest =>
        _inFlightQueue.Count < _effectivePipelineDepth && !IsReconnecting && !_outboundBodyPending &&
        !_connectionCloseReceived && !_draining;

    public bool HasInFlightRequests => _inFlightQueue.Count > 0;

    public bool IsReconnecting { get; private set; }

    public bool ShouldPauseNetwork => _heldBuffer is not null || (_activeStreamingReader?.IsFull ?? false);

    internal int PendingRequestCount
    {
        get
        {
            if (IsReconnecting)
            {
                return _reconnectBufferedQueue?.Count ?? 0;
            }

            return _inFlightQueue.Count;
        }
    }

    internal RequestEndpoint Endpoint { get; private set; }

    public Http11ClientStateMachine(
        IClientStageOperations ops,
        TurboClientOptions options)
    {
        _ops = ops;
        _options = options;

        var decoderOpts = options.ToHttp11DecoderOptions();
        var encoderOpts = options.ToHttp11EncoderOptions();

        _decoder = new Http11ClientDecoder(decoderOpts);
        _encoder = new Http11ClientEncoder(encoderOpts);
        // Pipeline depth is a connection concern, not a decoder concern — read it straight from options.
        _effectivePipelineDepth = options.Http1.MaxPipelineDepth;
    }

    public void PreStart()
    {
    }

    public void OnRequest(HttpRequestMessage request)
    {
        _inFlightQueue.Enqueue(request);

        var endpoint = RequestEndpoint.FromRequest(request);

        if (Endpoint == default && endpoint != default)
        {
            Endpoint = endpoint;
            _transportOptions = OptionsFactory.Build(Endpoint, _options);
            _ops.OnOutbound(new ConnectTransport(_transportOptions));
        }

        TransportBuffer? item = null;
        try
        {
            var contentLength = Convert.ToInt32(request.Content?.Headers.ContentLength ?? 0);
            item = TransportBuffer.Rent(HttpMessageSize.Estimate(request, contentLength));
            var span = item.FullMemory.Span;

            item.Length = _encoder.Encode(span, request, out var bodyStream, out var bodyContentLength);
            _ops.OnOutbound(new TransportData(item));

            if (bodyStream is not null)
            {
                _outboundBodyPending = true;
                StartBodyDrain(bodyStream, bodyContentLength, request.Version);
            }
        }
        catch (Exception ex)
        {
            item?.Dispose();
            Tracing.For("Protocol").Error(this, "Failed to encode HTTP/1.1 request [{0}]: {1}", request.RequestUri,
                ex.Message);
            request.Fail(ex);
            var count = _inFlightQueue.Count;
            for (var i = 0; i < count; i++)
            {
                var queued = _inFlightQueue.Dequeue();
                if (!ReferenceEquals(queued, request))
                {
                    _inFlightQueue.Enqueue(queued);
                }
            }
        }
    }

    public void OnRequestCancelled(HttpRequestMessage request)
    {
        var found = false;
        var temp = new Queue<HttpRequestMessage>();
        while (_inFlightQueue.Count > 0)
        {
            var queued = _inFlightQueue.Dequeue();
            if (ReferenceEquals(queued, request))
            {
                found = true;
                request.Fail(new OperationCanceledException("Request cancelled by caller."));
            }
            else
            {
                temp.Enqueue(queued);
            }
        }

        while (temp.Count > 0)
        {
            _inFlightQueue.Enqueue(temp.Dequeue());
        }

        if (!found)
        {
            return;
        }

        if (_inFlightQueue.Count == 0)
        {
            _ops.OnOutbound(new DisconnectTransport(DisconnectReason.Graceful));
            return;
        }

        _draining = true;
        Tracing.For("Protocol").Debug(this, "HTTP/1.1: cancelled request, draining {0} remaining",
            _inFlightQueue.Count);
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
        _decoder.SignalEof();

        if (_pendingBodyResponse is not null)
        {
            CompleteResponse(_pendingBodyResponse);
            _pendingBodyResponse = null;
        }
        else if (_decoder.IsBodyComplete)
        {
            var response = _decoder.GetResponse();
            CompleteResponse(response);
        }

        if (IsReconnecting)
        {
            if (_reconnectBufferedQueue is { Count: > 0 })
            {
                RequestFault.FailAll(_reconnectBufferedQueue,
                    new HttpRequestException("HTTP/1.1 transport closed during reconnect."));
            }

            IsReconnecting = false;
            _reconnectAttempts = 0;
            Tracing.For("Protocol").Debug(this, "HTTP/1.1 transport closed during reconnect");
            return;
        }

        TryDecodeEof();
        FailOrphanedRequests();
    }

    public void OnTimerFired(string name)
    {
    }

    public void OnBodyMessage(object msg)
    {
        Tracing.For("Protocol").Debug(this, "OnBodyMessage: {0}", msg.GetType().Name);
        switch (msg)
        {
            case StreamingSlotFreed:
                if (_heldBuffer is not null)
                {
                    var buf = _heldBuffer;
                    var off = _heldBufferOffset;
                    _heldBuffer = null;
                    _heldBufferOffset = 0;
                    DecodeResponse(buf, off);
                }

                break;

            case BodyReadComplete { BytesRead: > 0 } read:
                _currentWriter!.Advance(read.BytesRead);
                _currentWriter.FlushAsync();
                Tracing.For("Protocol").Trace(this, "request body chunk flushed (bytes={0})", read.BytesRead);
                ReadNextChunk();
                break;

            case BodyReadComplete { BytesRead: 0 }:
                _currentWriter!.CompleteAsync();
                _outboundBodyPending = false;
                _currentWriter = null;
                _currentBodyStream = null;
                Tracing.For("Protocol").Debug(this, "request body complete");
                break;

            case BodyReadFailed failed:
                Tracing.For("Protocol").Warning(this, "request body failed: {0}", failed.Reason.Message);
                _outboundBodyPending = false;
                _currentWriter?.Dispose();
                _currentWriter = null;
                _currentBodyStream = null;
                if (_inFlightQueue.Count > 0)
                {
                    var req = _inFlightQueue.Dequeue();
                    req.Fail(new HttpRequestException("Failed to encode HTTP/1.1 request body.", failed.Reason));
                }

                break;
        }
    }

    public void OnOutboundFlushed()
    {
    }

    public void Cleanup()
    {
        _inFlightQueue.Clear();
        _pendingBodyResponse?.Dispose();
        _pendingBodyResponse = null;
        _outboundBodyPending = false;
        _activeStreamingReader = null;
        _heldBuffer?.Dispose();
        _heldBuffer = null;
        _heldBufferOffset = 0;
        _connectionCloseReceived = false;
        _draining = false;
        _currentWriter?.Dispose();
        _currentWriter = null;
        _currentBodyStream = null;
        _pool.Dispose();
        _decoder.Reset();
    }

    private void DecodeResponse(TransportBuffer buffer, int startOffset = 0)
    {
        var memory = buffer.Memory;
        var offset = startOffset;
        var bufferHeld = false;
        try
        {
            while (offset < memory.Length)
            {
                var isHead = _inFlightQueue.Count > 0 && _inFlightQueue.Peek().Method == HttpMethod.Head;
                var outcome = _decoder.Feed(memory[offset..], isHead, out var consumed);
                offset += consumed;

                if (outcome == DecodeOutcome.NeedMore)
                {
                    if (_decoder.IsBodyStreaming && _pendingBodyResponse is null)
                    {
                        _pendingBodyResponse = _decoder.GetResponse();
                        if (_inFlightQueue.Count > 0)
                        {
                            _pendingBodyResponse.RequestMessage = _inFlightQueue.Peek();
                        }

                        _ops.OnResponse(_pendingBodyResponse);

                        if (_activeStreamingReader is null && _decoder.StreamingReader is { } sr)
                        {
                            _activeStreamingReader = sr;
                            sr.SlotFreed += () =>
                                _ops.StageActor.Tell(new StreamingSlotFreed(), ActorRefs.NoSender);
                        }
                    }

                    if (_decoder.IsQueueFull && offset < memory.Length)
                    {
                        _heldBuffer = buffer;
                        _heldBufferOffset = offset;
                        bufferHeld = true;
                    }

                    return;
                }

                if (outcome == DecodeOutcome.Complete)
                {
                    if (_pendingBodyResponse is not null)
                    {
                        _pendingBodyResponse = null;
                        _activeStreamingReader = null;
                        if (_inFlightQueue.Count > 0)
                        {
                            _inFlightQueue.Dequeue();
                        }

                        if (_draining && _inFlightQueue.Count == 0)
                        {
                            _draining = false;
                        }

                        _decoder.Reset();
                        continue;
                    }

                    var response = _decoder.GetResponse();

                    if ((int)response.StatusCode is >= 100 and < 200)
                    {
                        _decoder.Reset();
                        continue;
                    }

                    CompleteResponse(response);
                    _decoder.Reset();
                }
            }
        }
        catch (Exception ex)
        {
            Tracing.For("Protocol").Error(this, "Failed to decode HTTP/1.1 response: {0}", ex.Message);
            if (_inFlightQueue.Count > 0)
            {
                var req = _inFlightQueue.Dequeue();
                req.Fail(new HttpRequestException("Failed to decode HTTP/1.1 response.", ex));
            }

            _pendingBodyResponse = null;
            _activeStreamingReader = null;
            _decoder.Reset();
        }
        finally
        {
            if (!bufferHeld)
            {
                buffer.Dispose();
            }
        }
    }

    private void StartBodyDrain(Stream bodyStream, long? contentLength, Version httpVersion)
    {
        var (writer, _) = _pool.RentWriter(
            hasBody: true, contentLength, httpVersion,
            new BodyEncoderOptions { ChunkSize = _options.RequestBodyChunkSize },
            send: (owner, framedData) =>
            {
                var ownerSpan = owner.Memory.Span;
                var framedSpan = framedData.Span;
                ref var ownerStart = ref MemoryMarshal.GetReference(ownerSpan);
                ref var framedStart = ref MemoryMarshal.GetReference(framedSpan);
                var offset = (int)Unsafe.ByteOffset(ref ownerStart, ref framedStart);
                var buf = TransportBuffer.Wrap(owner, offset, framedData.Length);
                _ops.OnOutbound(new TransportData(buf));
                return default;
            });

        _currentWriter = writer;
        _currentBodyStream = bodyStream;
        Tracing.For("Protocol").Debug(this, "StartBodyDrain: writer={0}, contentLength={1}", writer?.GetType().Name, contentLength);
        ReadNextChunk();
    }

    private void ReadNextChunk()
    {
        var mem = _currentWriter!.GetMemory();
        _currentBodyStream!.ReadAsync(mem).AsTask().PipeTo(
            _ops.StageActor,
            success: bytesRead => new BodyReadComplete(bytesRead),
            failure: ex => new BodyReadFailed(ex));
    }

    private void HandleDisconnect(TransportDisconnected disconnect)
    {
        var isGraceful = disconnect.Reason == DisconnectReason.Graceful;

        if (isGraceful)
        {
            if (_pendingBodyResponse is not null)
            {
                _decoder.SignalEof();
                if (_inFlightQueue.Count > 0)
                {
                    _inFlightQueue.Dequeue();
                }

                _pendingBodyResponse = null;
                _activeStreamingReader = null;
            }
            else if (_decoder.HasActiveBody)
            {
                if (_decoder.SignalEof())
                {
                    var response = _decoder.GetResponse();
                    CompleteResponse(response);
                }
                else if (_inFlightQueue.Count > 0)
                {
                    var req = _inFlightQueue.Dequeue();
                    req.Fail(new HttpRequestException(
                        "HTTP/1.1 response body truncated: server closed before all bytes were received."));
                }
            }

            _decoder.Reset();
            return;
        }

        if (_pendingBodyResponse is not null)
        {
            _pendingBodyResponse = null;
            _activeStreamingReader = null;
            _decoder.Reset();
            if (_inFlightQueue.Count > 0)
            {
                var req = _inFlightQueue.Dequeue();
                req.Fail(new HttpRequestException("Connection closed while receiving HTTP/1.1 response body."));
            }
        }

        if (HasInFlightRequests && _options.Http1.MaxReconnectAttempts > 0)
        {
            Tracing.For("Protocol").Info(this, "HTTP/1.1 closed, {0} pending — reconnecting", PendingRequestCount);
            StartReconnect();
            return;
        }

        if (HasInFlightRequests)
        {
            const string message = "Connection was aborted while receiving HTTP/1.1 response.";
            RequestFault.FailAll(_inFlightQueue, new HttpRequestException(message));
            _inFlightQueue.Clear();
            Tracing.For("Protocol").Info(this, "HTTP/1.1: {0}", message);
        }

        _decoder.Reset();
    }

    private void TryDecodeEof()
    {
        try
        {
            if (_pendingBodyResponse is not null)
            {
                CompleteResponse(_pendingBodyResponse);
                _pendingBodyResponse = null;
            }
            else if (_decoder.IsBodyComplete)
            {
                var response = _decoder.GetResponse();
                CompleteResponse(response);
            }
        }
        catch (Exception ex)
        {
            Tracing.For("Protocol").Error(this, "Failed to decode HTTP/1.1 EOF: {0}", ex.Message);
        }
        finally
        {
            _decoder.Reset();
        }
    }

    private void FailOrphanedRequests()
    {
        if (_inFlightQueue.Count > 0)
        {
            Tracing.For("Protocol").Error(this, "HTTP/1.1 connection closed with orphaned requests — failing");
            RequestFault.FailAll(_inFlightQueue,
                new HttpRequestException("HTTP/1.1 connection closed with orphaned requests."));
            _inFlightQueue.Clear();
        }
    }

    private void StartReconnect()
    {
        _reconnectBufferedQueue = new Queue<HttpRequestMessage>(_inFlightQueue);
        _inFlightQueue.Clear();
        IsReconnecting = true;
        _reconnectAttempts = 1;
        _ops.OnOutbound(new ConnectTransport(_transportOptions!));
    }

    private void OnConnectionRestored()
    {
        IsReconnecting = false;
        _reconnectAttempts = 0;
        _connectionCloseReceived = false;
        _decoder.Reset();

        if (_reconnectBufferedQueue is { Count: > 0 })
        {
            var queue = _reconnectBufferedQueue;
            _reconnectBufferedQueue = null;

            while (queue.Count > 0)
            {
                var req = queue.Dequeue();
                OnRequest(req);
            }
        }
    }

    private void OnReconnectAttemptFailed()
    {
        if (_reconnectAttempts >= _options.Http1.MaxReconnectAttempts)
        {
            Tracing.For("Protocol").Info(this, "HTTP/1.1 reconnect failed after {0} attempts", _reconnectAttempts);
            if (_reconnectBufferedQueue is { Count: > 0 })
            {
                RequestFault.FailAll(_reconnectBufferedQueue,
                    new HttpRequestException("HTTP/1.1 reconnect failed after max attempts."));
                _reconnectBufferedQueue.Clear();
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
        if (_decoder.ConnectionWillClose)
        {
            _connectionCloseReceived = true;
        }

        HttpRequestMessage? request = null;
        if (_inFlightQueue.Count > 0)
        {
            request = _inFlightQueue.Dequeue();
        }

        if (_draining && _inFlightQueue.Count == 0)
        {
            _draining = false;
        }

        if (request is not null)
        {
            response.RequestMessage = request;
        }

        _ops.OnResponse(response);
    }
}