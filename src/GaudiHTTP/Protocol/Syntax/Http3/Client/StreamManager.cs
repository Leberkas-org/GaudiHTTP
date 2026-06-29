using System.Buffers;
using Akka.Actor;
using Servus.Akka.Transport;
using GaudiHTTP.Internal;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http3.Qpack;
using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Pooling;
using GaudiHTTP.Streams.Stages.Client;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Syntax.Http3.Client;

/// <summary>
/// Manages per-stream response assembly, request-response correlation, and
/// frame-decoder / stream-state pooling for an HTTP/3 connection.
/// Extracted from <see cref="Http3ClientStateMachine"/> for single-responsibility.
/// </summary>
internal sealed class StreamManager(
    IClientStageOperations ops,
    Http3ClientDecoder responseDecoder,
    QpackTableSync tableSync,
    long maxResponseBodySize)
{
    private readonly ConnectionObjectPool _objectPool = new();
    private readonly Dictionary<long, StreamState> _streams = new();
    private readonly Dictionary<long, HttpRequestMessage> _correlationMap = new();

    private readonly Dictionary<long, FrameDecoder> _streamDecoders = new();

    /// <summary>Whether there are in-flight requests awaiting responses.</summary>
    public bool HasInFlightRequests => _correlationMap.Count > 0 || _streams.Count > 0;

    /// <summary>
    /// Decodes a TransportBuffer into HTTP/3 frames using a per-stream decoder.
    /// Each QUIC stream has independent framing, so decoders must not share
    /// partial-frame remainder state across streams.
    /// Decoded frames may slice <paramref name="buffer"/> (zero-copy) — the caller owns
    /// the buffer and must dispose it only after all returned frames have been handled.
    /// </summary>
    public IReadOnlyList<Http3Frame> DecodeServerData(TransportBuffer buffer, long streamId)
    {
        if (!_streamDecoders.TryGetValue(streamId, out var decoder))
        {
            decoder = RentDecoder();
            _streamDecoders[streamId] = decoder;
        }

        return decoder.DecodeAll(buffer.Memory, out _);
    }

    /// <summary>
    /// Assembles a response from an HTTP/3 frame (HEADERS or DATA) on the given stream.
    /// </summary>
    public void AssembleResponse(Http3Frame frame, long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var state))
        {
            state = RentStreamState(streamId);
            _streams[streamId] = state;
        }

        switch (frame)
        {
            case HeadersFrame headers:
                HandleResponseHeaders(headers, state);
                break;

            case DataFrame data:
                HandleResponseData(data, state);
                break;
        }
    }

    /// <summary>
    /// Completes response assembly for a specific stream (QUIC FIN on request stream).
    /// </summary>
    public void FlushPendingResponse(long streamId)
    {
        if (_streams.TryGetValue(streamId, out var state) && state.HasBodyReader)
        {
            state.FeedBody(ReadOnlySpan<byte>.Empty, endStream: true);
            state.DetachBodyReader();
            ReturnStreamState(streamId);
            return;
        }

        if (state is { IsHeadersBlocked: true })
        {
            // FIN arrived while still QPACK-blocked; remember it so ResolveBlockedStreams can
            // complete the body after the headers (and any buffered DATA) are delivered.
            state.PendingEndStream = true;
            return;
        }

        if (state is { HasResponse: true })
        {
            EmitResponse(streamId);
        }
    }

    /// <summary>
    /// Fails an in-flight request on the given stream due to a transport error.
    /// Removes the correlation and stream state, and completes the <see cref="PendingRequest"/>
    /// with an exception so the caller's <c>SendAsync</c> or <c>ReadAsStringAsync</c> throws.
    /// Returns true if a correlated request was found and failed.
    /// </summary>
    public void FailInflightRequest(long streamId, Exception exception)
    {
        if (_streams.TryGetValue(streamId, out var state))
        {
            AbortAndReturnBodyReader(state);
            _objectPool.Return(state);
            _streams.Remove(streamId);
        }

        if (!_correlationMap.Remove(streamId, out var request))
        {
            return;
        }

        OnStreamClosedCallback?.Invoke(streamId);
        ReturnDecoder(streamId);

        if (request.Options.TryGetValue(OptionsKey.Key, out var pending)
            && request.Options.TryGetValue(OptionsKey.VersionKey, out var ver))
        {
            pending.TrySetException(exception, ver);
        }
    }

    /// <summary>
    /// Completes all in-progress response assemblies (upstream finish / connection close).
    /// </summary>
    public void FlushAllPendingResponses()
    {
        var handledStreamIds = new HashSet<long>();

        foreach (var (streamId, state) in _streams)
        {
            if (state.HasBodyReader)
            {
                state.FeedBody(ReadOnlySpan<byte>.Empty, endStream: true);
                state.DetachBodyReader();
                handledStreamIds.Add(streamId);
            }
        }

        var streamIds = ArrayPool<long>.Shared.Rent(_streams.Count);
        var streamCount = 0;
        foreach (var key in _streams.Keys)
        {
            streamIds[streamCount++] = key;
        }

        for (var i = 0; i < streamCount; i++)
        {
            if (handledStreamIds.Contains(streamIds[i]))
            {
                continue;
            }

            if (_streams.TryGetValue(streamIds[i], out var state) && state.HasResponse)
            {
                EmitResponse(streamIds[i]);
            }
        }

        ArrayPool<long>.Shared.Return(streamIds);
    }

    /// <summary>
    /// Resolves blocked streams after QPACK encoder instructions arrive.
    /// </summary>
    public void ResolveBlockedStreams(
        IReadOnlyList<(int StreamId, IReadOnlyList<(string Name, string Value)> Headers)> resolved)
    {
        foreach (var (streamId, headers) in resolved)
        {
            if (_streams.TryGetValue(streamId, out var state))
            {
                if (!state.HasResponse)
                {
                    if (!responseDecoder.AssembleHeaders(headers, state)
                        && responseDecoder.LastInterimResponse is { } interim)
                    {
                        responseDecoder.LastInterimResponse = null;
                        interim.RequestMessage = _correlationMap.GetValueOrDefault(state.StreamId);
                        ops.OnResponse(interim);
                    }
                }

                if (state is { HasResponse: true, HasBodyReader: false })
                {
                    var queued = _objectPool.Rent(() => new QueuedBodyReader(capacity: 8));
                    state.InitBodyReader(queued, maxResponseBodySize);
                    var response = state.GetResponse();
                    var stageActor = ops.StageActor;
                    var capturedId = streamId;
                    var bodyStream = queued.AsStream(onAbandoned: () =>
                        stageActor.Tell(new Http3ClientSessionManager.AbandonedResponseBody(capturedId), ActorRefs.NoSender));
                    response.Content = new StreamContent(bodyStream);
                    state.ApplyContentHeadersTo(response.Content);

                    if (_correlationMap.Remove(streamId, out var request))
                    {
                        response.RequestMessage = request;
                    }

                    var partialContentResult = PartialContentValidator.Validate(response);
                    if (!partialContentResult.IsValid)
                    {
                        Tracing.For("Protocol").Warning(this, "{0}", partialContentResult.ErrorMessage!);
                    }

                    ops.OnResponse(response);

                    // Replay DATA buffered while the stream was blocked, then honor a FIN that
                    // arrived during the block so the body completes.
                    state.IsHeadersBlocked = false;
                    state.ReplayPendingInboundData();

                    if (state.PendingEndStream)
                    {
                        state.FeedBody(ReadOnlySpan<byte>.Empty, endStream: true);
                        state.DetachBodyReader();
                        ReturnStreamState(streamId);
                    }
                }
            }
        }
    }

    public StreamState GetOrCreateStreamState(long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var state))
        {
            state = RentStreamState(streamId);
            _streams[streamId] = state;
        }

        return state;
    }

    /// <summary>
    /// Returns the stream state for the given stream ID, or null if not found.
    /// </summary>
    public StreamState? TryGetStreamState(long streamId)
    {
        return _streams.GetValueOrDefault(streamId);
    }

    /// <summary>
    /// Registers a request correlation for the given stream ID.
    /// </summary>
    public void Correlate(long streamId, HttpRequestMessage request)
    {
        _correlationMap[streamId] = request;
    }

    /// <summary>
    /// Removes the correlation for the given stream ID (used when a stream is cancelled or closed externally).
    /// </summary>
    public void RemoveCorrelation(long streamId)
    {
        _correlationMap.Remove(streamId);
    }

    /// <summary>
    /// Exposes the live correlation map for read-only inspection (e.g. reconnect, metrics).
    /// </summary>
    public IReadOnlyDictionary<long, HttpRequestMessage> GetCorrelationMap()
    {
        return _correlationMap;
    }

    /// <summary>
    /// Snapshots all correlated requests and clears the map (used on connection reset / GOAWAY).
    /// </summary>
    public List<HttpRequestMessage> SnapshotAndClearCorrelations()
    {
        var snapshot = new List<HttpRequestMessage>(_correlationMap.Values);
        _correlationMap.Clear();
        return snapshot;
    }

    /// <summary>
    /// Finds the stream ID correlated to <paramref name="request"/> by reference equality.
    /// Returns true and sets <paramref name="streamId"/> when found.
    /// </summary>
    public bool TryFindStreamByRequest(HttpRequestMessage request, out long streamId)
    {
        foreach (var (id, req) in _correlationMap)
        {
            if (ReferenceEquals(req, request))
            {
                streamId = id;
                return true;
            }
        }

        streamId = -1;
        return false;
    }

    /// <summary>
    /// Drains and pools all per-stream state. Keeps correlation map intact for reconnect.
    /// </summary>
    public void DrainStreams()
    {
        foreach (var (_, state) in _streams)
        {
            AbortAndReturnBodyReader(state);
            _objectPool.Return(state);
        }

        _streams.Clear();
    }

    /// <summary>
    /// Resets all frame decoders and returns them to the pool.
    /// </summary>
    public void ResetAllDecoders()
    {
        foreach (var decoder in _streamDecoders.Values)
        {
            _objectPool.Return(decoder);
        }

        _streamDecoders.Clear();
    }

    /// <summary>
    /// Disposes all owned resources (decoders, stream states, pools).
    /// </summary>
    public void Dispose()
    {
        ResetAllDecoders();

        foreach (var state in _streams.Values)
        {
            AbortAndReturnBodyReader(state);
            state.Reset();
        }

        _streams.Clear();
    }

    private void HandleResponseHeaders(HeadersFrame frame, StreamState state)
    {
        var result = tableSync.TryDecodeOrBlock(frame.HeaderBlock, (int)state.StreamId);

        if (result.IsBlocked)
        {
            // Mark blocked so DATA frames that arrive before the QPACK encoder instructions
            // resolve this stream are buffered (HandleResponseData) instead of dropped.
            state.IsHeadersBlocked = true;
            return;
        }

        if (!responseDecoder.AssembleHeaders(result.Headers!, state))
        {
            if (responseDecoder.LastInterimResponse is { } interim)
            {
                responseDecoder.LastInterimResponse = null;
                interim.RequestMessage = _correlationMap.GetValueOrDefault(state.StreamId);
                ops.OnResponse(interim);
            }

            return;
        }

        var streamId = state.StreamId;

        var queued = _objectPool.Rent(() => new QueuedBodyReader(capacity: 8));
        state.InitBodyReader(queued, maxResponseBodySize);
        var response = state.GetResponse();
        var stageActor = ops.StageActor;
        var capturedId = streamId;
        var bodyStream = queued.AsStream(onAbandoned: () =>
            stageActor.Tell(new Http3ClientSessionManager.AbandonedResponseBody(capturedId), ActorRefs.NoSender));
        response.Content = new StreamContent(bodyStream);
        state.ApplyContentHeadersTo(response.Content);

        if (_correlationMap.Remove(streamId, out var request))
        {
            response.RequestMessage = request;
        }

        var partialContentResult = PartialContentValidator.Validate(response);
        if (!partialContentResult.IsValid)
        {
            Tracing.For("Protocol").Warning(this, "{0}", partialContentResult.ErrorMessage!);
        }

        ops.OnResponse(response);
    }

    private void HandleResponseData(DataFrame frame, StreamState state)
    {
        if (!state.HasBodyReader)
        {
            if (state.IsHeadersBlocked)
            {
                // HEADERS are QPACK-blocked; buffer the DATA (copied — it aliases the pooled
                // transport buffer) and replay it once ResolveBlockedStreams initializes the
                // body reader. Dropping here would silently truncate the response body.
                state.BufferInboundData(frame.Data.Span);
                return;
            }

            Tracing.For("Protocol").Warning(this, "RFC 9114 §4.1 - DATA frame received before HEADERS; dropping.");
            return;
        }

        state.FeedBody(frame.Data.Span, false);
    }

    private void EmitResponse(long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var state) || !state.HasResponse)
        {
            return;
        }

        var response = state.GetResponse();

        if (response.Content is null)
        {
            response.Content = new ByteArrayContent([]);
            state.ApplyContentHeadersTo(response.Content);
        }

        if (_correlationMap.Remove(streamId, out var request))
        {
            response.RequestMessage = request;
        }

        var partialContentResult = PartialContentValidator.Validate(response);
        if (!partialContentResult.IsValid)
        {
            Tracing.For("Protocol").Warning(this, "{0}", partialContentResult.ErrorMessage!);
        }

        ops.OnResponse(response);

        ReturnStreamState(streamId);
    }

    public void OnResponseBodyAbandoned(long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var state))
        {
            return;
        }

        AbortAndReturnBodyReader(state);
        ReturnStreamState(streamId);
    }

    private void AbortAndReturnBodyReader(StreamState state)
    {
        var reader = state.TakeBodyReader();
        if (reader is IStreamingBodyReader streaming)
        {
            streaming.Fault(new OperationCanceledException());
        }

        if (reader is QueuedBodyReader queued)
        {
            _objectPool.Return(queued);
        }
        else
        {
            reader?.Dispose();
        }
    }

    private StreamState RentStreamState(long streamId)
    {
        var state = _objectPool.Rent(static () => new StreamState());
        state.Initialize(streamId);
        return state;
    }

    private void ReturnStreamState(long streamId)
    {
        if (!_streams.Remove(streamId, out var state))
        {
            return;
        }

        OnStreamClosedCallback?.Invoke(streamId);

        _objectPool.Return(state);
        ReturnDecoder(streamId);
    }

    private FrameDecoder RentDecoder()
    {
        return _objectPool.Rent(static () => new FrameDecoder());
    }

    private void ReturnDecoder(long streamId)
    {
        if (_streamDecoders.Remove(streamId, out var decoder))
        {
            _objectPool.Return(decoder);
        }
    }

    /// <summary>
    /// Callback invoked when a stream is closed (response emitted).
    /// The StateMachine uses this to update <see cref="StreamTracker"/> and <see cref="ConnectionState"/>.
    /// </summary>
    internal Action<long>? OnStreamClosedCallback { get; init; }
}
