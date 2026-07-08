using System.Buffers;
using System.Net;
using Akka.Actor;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Internal;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Streams.Stages.Client;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Syntax.Http11.Client;

internal sealed class Http11ClientStateMachine : IClientStateMachine, IBodyDrainTarget
{
    private readonly IClientStageOperations _ops;
    private readonly Http11ClientDecoder _decoder;
    private readonly Http11ClientEncoder _encoder;
    private readonly GaudiClientOptions _options;

    private readonly Queue<HttpRequestMessage> _inFlightQueue = new();
    private readonly ReconnectPolicy<Queue<HttpRequestMessage>> _reconnectPolicy;
    private readonly int _effectivePipelineDepth;
    private TransportOptions? _transportOptions;
    private HttpResponseMessage? _pendingBodyResponse;
    private bool _outboundBodyPending;
    private bool _isChunked;
    private IStreamingBodyReader? _activeStreamingReader;
    private WireBuffer? _heldBuffer;
    private int _heldBufferOffset;
    private WireBuffer? _partialResponse;
    private ConnectionState _connectionState;
    private SerialBodyPump? _serialPump;
    private CancellationTokenSource? _connectionCts;

    internal sealed record StreamingSlotFreed;

    // Connection lifecycle. Replaces four independently-tracked booleans
    // (IsReconnecting, _connectionCloseReceived, _draining, _connectionDead) with one explicit
    // state; `Active` is the default (enum default value 0).
    //
    // Transition table:
    //   Active                        -- construction; also OnConnectionRestored() and Cleanup()
    //                                    return here (Cleanup only if not currently Reconnecting)
    //   Active -> CloseAfterResponses     CompleteResponse() when the decoder reports
    //                                     Connection: close. No new requests are accepted; requests
    //                                     already in flight still drain normally.
    //   Active | CloseAfterResponses
    //     -> Reconnecting                 StartReconnect(), from HandleDisconnect() on an
    //                                     ungraceful disconnect with in-flight requests and
    //                                     reconnect attempts configured.
    //   Reconnecting -> Active            OnConnectionRestored() (TransportConnected), or
    //                                     OnUpstreamFinished() abandoning a reconnect in progress
    //                                     because the stage itself is shutting down.
    //   Reconnecting -> Dead              OnReconnectAttemptFailed() once MaxReconnectAttempts is
    //                                     exhausted.
    //
    // Findings from the boolean inventory (Task 9):
    //   - `_draining` was never assigned `true` anywhere in this class — both of its guarded
    //     branches (in DecodeResponse and CompleteResponse) were unreachable dead code and are
    //     dropped here; behavior is unchanged since those branches never executed.
    //   - CloseAfterResponses and Reconnecting could, in principle, both be "true" under the old
    //     independent-booleans model (nothing cleared _connectionCloseReceived when StartReconnect
    //     flipped IsReconnecting). In practice this combination is unreachable: Akka actor thread
    //     confinement processes TransportConnected (which resets to Active) fully before any
    //     subsequent TransportData is decoded, so CompleteResponse can never run while
    //     Reconnecting. Collapsing both bits into one field is therefore safe — CompleteResponse
    //     only promotes Active -> CloseAfterResponses, never overwriting Reconnecting/Dead.
    //   - Cleanup() never reset IsReconnecting in the original code (only the other three flags).
    //     Preserved below by leaving the state untouched when it is already Reconnecting.
    private enum ConnectionState
    {
        Active,
        CloseAfterResponses,
        Reconnecting,
        Dead
    }

    public bool CanAcceptRequest =>
        _inFlightQueue.Count < _effectivePipelineDepth && !_outboundBodyPending &&
        _connectionState == ConnectionState.Active;

    public bool HasInFlightRequests => _inFlightQueue.Count > 0;

    public bool IsReconnecting => _connectionState == ConnectionState.Reconnecting;

    public bool ShouldPauseNetwork => _heldBuffer is not null || (_activeStreamingReader?.IsFull ?? false);

    internal int PendingRequestCount
    {
        get
        {
            if (IsReconnecting)
            {
                return _reconnectPolicy.Buffered?.Count ?? 0;
            }

            return _inFlightQueue.Count;
        }
    }

    internal RequestEndpoint Endpoint { get; private set; }

    public Http11ClientStateMachine(
        GaudiClientOptions options,
        IClientStageOperations ops)
    {
        _ops = ops;
        _options = options;
        _reconnectPolicy = new ReconnectPolicy<Queue<HttpRequestMessage>>(
            ops,
            options.Http1.MaxReconnectAttempts,
            options.Http1.ReconnectInitialBackoff,
            options.Http1.ReconnectMaxBackoff,
            options.Http1.ReconnectBackoffMultiplier,
            options.Http1.ReconnectBackoffJitter);

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

    private CancellationTokenSource EnsureConnectionCts()
    {
        return _connectionCts ??= new CancellationTokenSource();
    }

    IActorRef IBodyDrainTarget.StageActor => _ops.StageActor;

    void IBodyDrainTarget.EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
    {
        if (!data.IsEmpty)
        {
            if (_isChunked)
            {
                var framedSize = ChunkedFramingHelper.GetFramedSize(data.Length);
                var buf = WireBuffer.Rent(framedSize);
                ChunkedFramingHelper.WriteChunk(data.Span, buf.FullMemory.Span);
                buf.Length = framedSize;
                _ops.OnOutbound(TransportData.Rent(buf));
            }
            else
            {
                var buf = WireBuffer.Rent(data.Length);
                data.CopyTo(buf.FullMemory);
                buf.Length = data.Length;
                _ops.OnOutbound(TransportData.Rent(buf));
            }
        }

        if (endStream)
        {
            if (_isChunked)
            {
                var buf = WireBuffer.Rent(5);
                ChunkedFramingHelper.WriteTerminator(buf.FullMemory.Span);
                buf.Length = 5;
                _ops.OnOutbound(TransportData.Rent(buf));
            }

            _outboundBodyPending = false;
            Tracing.For("Protocol").Debug(this, "request body complete");
        }
        else
        {
            Tracing.For("Protocol").Trace(this, "request body chunk flushed (bytes={0})", data.Length);
        }
    }

    void IBodyDrainTarget.EmitOwnedDataFrames(int streamId, IMemoryOwner<byte> owner, int bytesWritten, bool endStream)
    {
        if (bytesWritten > 0)
        {
            if (_isChunked)
            {
                var framedSize = ChunkedFramingHelper.GetFramedSize(bytesWritten);
                var buf = WireBuffer.Rent(framedSize);
                ChunkedFramingHelper.WriteChunk(owner.Memory.Span[..bytesWritten], buf.FullMemory.Span);
                buf.Length = framedSize;
                _ops.OnOutbound(TransportData.Rent(buf));
                owner.Dispose();
            }
            else
            {
                _ops.OnOutbound(TransportData.Rent(WireBuffer.Wrap(owner, 0, bytesWritten)));
            }
        }
        else
        {
            owner.Dispose();
        }

        if (endStream)
        {
            if (_isChunked)
            {
                var buf = WireBuffer.Rent(5);
                ChunkedFramingHelper.WriteTerminator(buf.FullMemory.Span);
                buf.Length = 5;
                _ops.OnOutbound(TransportData.Rent(buf));
            }

            _outboundBodyPending = false;
            Tracing.For("Protocol").Debug(this, "request body complete");
        }
        else
        {
            Tracing.For("Protocol").Trace(this, "request body chunk flushed (bytes={0})", bytesWritten);
        }
    }

    void IBodyDrainTarget.OnDrainComplete(int streamId)
    {
        Tracing.For("Protocol").Debug(this, "request body drain complete");
    }

    void IBodyDrainTarget.OnDrainFailed(int streamId, Exception reason)
    {
        Tracing.For("Protocol").Warning(this, "request body failed: {0}", reason.Message);
        _outboundBodyPending = false;
        if (_inFlightQueue.Count > 0)
        {
            var req = _inFlightQueue.Dequeue();
            req.Fail(new HttpRequestException("Failed to encode HTTP/1.1 request body.", reason));
        }
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

        WireBuffer? item = null;
        try
        {
            // Build the request headers once and rent a buffer sized to exactly the request line +
            // header block. The body is streamed separately via StartBodyDrain, so it is NOT part of
            // this buffer — this avoids both the throwaway header build HttpMessageSize.Estimate did
            // purely for sizing and the body-sized over-rent it added on top.
            var headerSize = _encoder.Prepare(request, out var bodyStream, out var bodyContentLength);
            item = WireBuffer.Rent(headerSize);

            item.Length = _encoder.WriteTo(item.FullMemory.Span, request);
            _ops.OnOutbound(TransportData.Rent(item));

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
        // Do NOT remove the request from _inFlightQueue. The server will still
        // send a response for it, and removing it desyncs the queue from the
        // wire-order responses, corrupting subsequent request–response pairings.
        //
        // The PendingRequest TCS was already cancelled by the CTS registration
        // in SendAsync, so CompleteResponse's TrySetResult is harmless.
        //
        // Do NOT send DisconnectTransport: the engine only emits ConnectTransport
        // once (when Endpoint is first set), so a graceful disconnect leaves the
        // transport disconnected with no way to re-establish the connection.
        request.Fail(new OperationCanceledException("Request cancelled by caller."));
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

            case TransportDataFlushed flushed:
                // Real wire flush: credit the request-body pump by the bytes the transport actually
                // drained. Replaces the push-time OnOutboundFlushed "lie" with true byte back-pressure.
                _serialPump?.OnCapacityAvailable(flushed.Bytes);
                return;
        }

        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        // Prepend any unconsumed prefix retained from the previous read — an incomplete status line
        // or header line split across the read boundary — so the decoder resumes from it instead of
        // losing it (which would desync the connection and fault subsequent pipelined responses).
        if (_partialResponse is not null)
        {
            buffer = CombineWithPartial(buffer);
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
            if (_reconnectPolicy.TakeBuffered() is { Count: > 0 } buffered)
            {
                RequestFault.FailAll(buffered,
                    new HttpRequestException("HTTP/1.1 transport closed during reconnect."));
            }

            _connectionState = ConnectionState.Active;
            Tracing.For("Protocol").Debug(this, "HTTP/1.1 transport closed during reconnect");
            return;
        }

        TryDecodeEof();
        FailOrphanedRequests();
    }

    public void OnTimerFired(string name)
    {
        _reconnectPolicy.OnReconnectTimerFired(name);
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
        // Fail (don't silently drop) requests still in flight or buffered for reconnect replay, so
        // callers fault promptly on stage teardown instead of hanging until their client-side timeout.
        if (_inFlightQueue.Count > 0)
        {
            RequestFault.FailAll(_inFlightQueue,
                new HttpRequestException("HTTP/1.1 connection was torn down before the request completed."));
            _inFlightQueue.Clear();
        }

        if (_reconnectPolicy.TakeBuffered() is { Count: > 0 } bufferedForReplay)
        {
            RequestFault.FailAll(bufferedForReplay,
                new HttpRequestException("HTTP/1.1 connection was torn down before the buffered request could be replayed."));
        }

        _pendingBodyResponse?.Dispose();
        _pendingBodyResponse = null;
        _outboundBodyPending = false;
        _activeStreamingReader = null;
        _heldBuffer?.Dispose();
        _heldBuffer = null;
        _heldBufferOffset = 0;
        ClearPartial();
        if (_connectionState != ConnectionState.Reconnecting)
        {
            _connectionState = ConnectionState.Active;
        }
        _serialPump?.Cleanup();
        _serialPump = null;
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _connectionCts = null;
        _decoder.Reset();
    }

    private void DecodeResponse(WireBuffer buffer, int startOffset = 0)
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
                    else if (offset < memory.Length)
                    {
                        // Incomplete status line / header (or a split frame header) with no streaming
                        // back-pressure: retain the unconsumed prefix so it survives this buffer's
                        // disposal and is re-presented ahead of the next read.
                        RetainPartial(memory.Span[offset..]);
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

                        _decoder.Reset();
                        continue;
                    }

                    var response = _decoder.GetResponse();

                    if ((int)response.StatusCode is >= 100 and < 200)
                    {
                        if ((int)response.StatusCode is not 101)
                        {
                            _ops.OnResponse(response);
                        }

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
            // The byte stream is desynced after a decode failure; any retained prefix is now garbage.
            ClearPartial();
        }
        finally
        {
            if (!bufferHeld)
            {
                buffer.Dispose();
            }
        }
    }

    // Merges the retained partial prefix with the next inbound buffer into a single contiguous
    // buffer, disposing both inputs. The caller takes ownership of (and disposes) the result.
    private WireBuffer CombineWithPartial(WireBuffer incoming)
    {
        var partial = _partialResponse!;
        _partialResponse = null;

        var combined = WireBuffer.Rent(partial.Length + incoming.Length);
        partial.Span.CopyTo(combined.FullMemory.Span);
        incoming.Span.CopyTo(combined.FullMemory.Span[partial.Length..]);
        combined.Length = partial.Length + incoming.Length;

        partial.Dispose();
        incoming.Dispose();
        return combined;
    }

    // Copies the unconsumed prefix into a freshly rented buffer so it outlives the current
    // (about-to-be-disposed) inbound buffer. Bounded by the decoder's max header size.
    private void RetainPartial(ReadOnlySpan<byte> remainder)
    {
        var buf = WireBuffer.Rent(remainder.Length);
        remainder.CopyTo(buf.FullMemory.Span);
        buf.Length = remainder.Length;
        _partialResponse = buf;
    }

    private void ClearPartial()
    {
        _partialResponse?.Dispose();
        _partialResponse = null;
    }

    private void StartBodyDrain(Stream bodyStream, long? contentLength, Version httpVersion)
    {
        _isChunked = contentLength is null && !httpVersion.Equals(HttpVersion.Version10);
        Tracing.For("Protocol").Debug(this, "StartBodyDrain: chunked={0}, contentLength={1}", _isChunked, contentLength);

        _serialPump = new SerialBodyPump(this, EnsureConnectionCts(),
            _options.ResolveRequestBodyChunkSize(_options.Http1), maxBytes: 256 * 1024);
        _serialPump.Register(bodyStream, contentLength: null, CancellationToken.None);
    }

    private void HandleDisconnect(TransportDisconnected disconnect)
    {
        // The connection's byte stream is gone; a retained partial prefix from it is now stale.
        ClearPartial();

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

        if (HasInFlightRequests && _reconnectPolicy.CanReconnect)
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
        // Only idempotent in-flight requests are safe to replay: a non-idempotent request
        // (e.g. POST) may already have been received and processed by the server, so replaying
        // it after connection loss risks a duplicate side effect (RFC 9110 §9.2.2). Fail those
        // instead of buffering them. Mirrors the HTTP/2 IsStreamSafeToReplay gate.
        var buffered = new Queue<HttpRequestMessage>();
        while (_inFlightQueue.Count > 0)
        {
            var request = _inFlightQueue.Dequeue();
            if (MethodProperties.IsIdempotent(request.Method))
            {
                buffered.Enqueue(request);
            }
            else
            {
                Tracing.For("Protocol").Info(this,
                    "HTTP/1.1: not replaying non-idempotent request {0} {1} on reconnect (server may have already processed it)",
                    request.Method, request.RequestUri);
                request.Fail(new HttpRequestException(
                    "Non-idempotent HTTP/1.1 request was not replayed after connection loss; the server may have already processed it."));
            }
        }

        _connectionState = ConnectionState.Reconnecting;
        _reconnectPolicy.Start(buffered, _transportOptions!);
    }

    private void OnConnectionRestored()
    {
        // Capture the reconnect state BEFORE it is overwritten below: TransportConnected fires on the
        // INITIAL connect too (the client connects lazily inside the first OnRequest), not only on a
        // genuine reconnect.
        var wasReconnecting = _connectionState == ConnectionState.Reconnecting;
        _connectionState = ConnectionState.Active;
        _decoder.Reset();

        // Reset the request-body credit state ONLY on a genuine reconnect, before any buffered request
        // is re-armed. A request whose upload was interrupted leaves a stale, budget-depleted pump in
        // _serialPump; each replayed body-drain (StartBodyDrain) constructs a FRESH pump whose
        // Register() already fills the budget to maxBytes, so the replay never deadlocks on stale
        // credit. Tear the stale pump down here rather than ResetCredit()-ing it: reviving an
        // orphaned pump would make it read the old, partially-consumed body stream and emit those
        // stale bytes onto the freshly reconnected wire (out of order, ahead of the replayed
        // headers) — corrupting the connection. Disposing it also frees its rented buffer and
        // linked CTS.
        //
        // On the INITIAL connect this MUST NOT run: the first OnRequest already created and started
        // _serialPump for a bodied request (parked at the 256 KB budget for a large/async body).
        // TransportConnected necessarily precedes any TransportDataFlushed, so tearing the pump down
        // here would strand the still-draining first upload — later flushes hit a null pump, end-stream
        // never fires, and the request hangs (silent truncation).
        if (wasReconnecting)
        {
            _serialPump?.Cleanup();
            _serialPump = null;
        }

        if (_reconnectPolicy.TakeBuffered() is { Count: > 0 } queue)
        {
            while (queue.Count > 0)
            {
                var req = queue.Dequeue();

                // The encoder re-reads the body via HttpContent.ReadAsStream(), which returns the
                // SAME cached stream now sitting at EOF from the interrupted first attempt. Rewind a
                // seekable body to the start so the replay re-sends it in full; fail fast on a
                // consumed forward-only body instead of advertising the full Content-Length while
                // emitting a truncated body (which would hang a fixed-length server read).
                if (!RequestBodyReplay.TryRewindForReplay(req))
                {
                    Tracing.For("Protocol").Warning(this,
                        "HTTP/1.1: cannot replay {0} {1} after reconnect — request body is not rewindable",
                        req.Method, req.RequestUri);
                    req.Fail(new HttpRequestException(
                        "HTTP/1.1 request body could not be replayed after connection loss: the content stream is not rewindable."));
                    continue;
                }

                OnRequest(req);
            }
        }
    }

    private void OnReconnectAttemptFailed()
    {
        var attemptsAtFailure = _reconnectPolicy.Attempts;

        if (_reconnectPolicy.OnAttemptFailed(_transportOptions!, out var buffered))
        {
            Tracing.For("Protocol").Info(this, "HTTP/1.1 reconnect failed after {0} attempts", attemptsAtFailure);
            if (buffered is { Count: > 0 })
            {
                RequestFault.FailAll(buffered,
                    new HttpRequestException("HTTP/1.1 reconnect failed after max attempts."));
            }

            _connectionState = ConnectionState.Dead;
        }
    }

    private void CompleteResponse(HttpResponseMessage response)
    {
        if (_decoder.ConnectionWillClose && _connectionState == ConnectionState.Active)
        {
            _connectionState = ConnectionState.CloseAfterResponses;
        }

        HttpRequestMessage? request = null;
        if (_inFlightQueue.Count > 0)
        {
            request = _inFlightQueue.Dequeue();
        }

        if (request is not null)
        {
            response.RequestMessage = request;
        }

        _ops.OnResponse(response);
    }
}