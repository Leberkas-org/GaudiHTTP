using System.Buffers;
using Servus.Akka.Transport;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Client;
using static Servus.Senf;

namespace TurboHTTP.Protocol.Syntax.Http3.Client;

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
    private const int MaxPoolSize = 256;
    private const int MaxDecoderPoolSize = 256;

    private readonly Dictionary<long, StreamState> _streams = new();
    private readonly Dictionary<long, HttpRequestMessage> _correlationMap = new();
    private readonly Stack<StreamState> _statePool = new();

    private readonly Dictionary<long, FrameDecoder> _streamDecoders = new();
    private readonly Stack<FrameDecoder> _decoderPool = new();

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
            state.AbortBody();
            state.Reset();
            if (_statePool.Count < MaxPoolSize)
            {
                _statePool.Push(state);
            }

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
                    responseDecoder.AssembleHeaders(headers, state);
                }

                if (state is { HasResponse: true, HasBodyReader: false })
                {
                    var queued = new QueuedBodyReader(capacity: 8);
                    queued.Reset();
                    state.InitBodyReader(queued, maxResponseBodySize);
                    var response = state.GetResponse();
                    var bodyStream = state.GetBodyStream();
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
    /// Drains and pools all per-stream state. Keeps correlation map intact for reconnect.
    /// </summary>
    public void DrainStreams()
    {
        foreach (var (_, state) in _streams)
        {
            state.Reset();
            if (_statePool.Count < MaxPoolSize)
            {
                _statePool.Push(state);
            }
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
            decoder.Reset();
            if (_decoderPool.Count < MaxDecoderPoolSize)
            {
                _decoderPool.Push(decoder);
            }
            else
            {
                decoder.Dispose();
            }
        }

        _streamDecoders.Clear();
    }

    /// <summary>
    /// Disposes all owned resources (decoders, stream states, pools).
    /// </summary>
    public void Dispose()
    {
        ResetAllDecoders();

        foreach (var decoder in _decoderPool)
        {
            decoder.Dispose();
        }

        _decoderPool.Clear();

        foreach (var state in _streams.Values)
        {
            state.Reset();
        }

        _streams.Clear();

        while (_statePool.TryPop(out _))
        {
        }
    }

    private void HandleResponseHeaders(HeadersFrame frame, StreamState state)
    {
        var result = tableSync.TryDecodeOrBlock(frame.HeaderBlock, (int)state.StreamId);

        if (result.IsBlocked)
        {
            return;
        }

        if (!responseDecoder.AssembleHeaders(result.Headers!, state))
        {
            return;
        }

        var streamId = state.StreamId;

        var queued = new QueuedBodyReader(capacity: 8);
        queued.Reset();
        state.InitBodyReader(queued, maxResponseBodySize);
        var response = state.GetResponse();
        var bodyStream = state.GetBodyStream();
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

    private StreamState RentStreamState(long streamId)
    {
        var state = _statePool.TryPop(out var pooled) ? pooled : new StreamState();
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

        state.Reset();
        if (_statePool.Count < MaxPoolSize)
        {
            _statePool.Push(state);
        }

        ReturnDecoder(streamId);
    }

    private FrameDecoder RentDecoder()
    {
        if (_decoderPool.TryPop(out var decoder))
        {
            decoder.Reset();
            return decoder;
        }

        return new FrameDecoder();
    }

    private void ReturnDecoder(long streamId)
    {
        if (!_streamDecoders.Remove(streamId, out var decoder))
        {
            return;
        }

        decoder.Reset();
        if (_decoderPool.Count < MaxDecoderPoolSize)
        {
            _decoderPool.Push(decoder);
        }
        else
        {
            decoder.Dispose();
        }
    }

    /// <summary>
    /// Callback invoked when a stream is closed (response emitted).
    /// The StateMachine uses this to update <see cref="StreamTracker"/> and <see cref="ConnectionState"/>.
    /// </summary>
    internal Action<long>? OnStreamClosedCallback { get; init; }
}
