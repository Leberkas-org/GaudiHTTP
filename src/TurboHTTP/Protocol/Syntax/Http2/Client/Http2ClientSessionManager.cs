using System.Buffers;
using Akka.Actor;
using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Multiplexed;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http2.Options;
using TurboHTTP.Streams.Stages.Client;
using static Servus.Senf;

namespace TurboHTTP.Protocol.Syntax.Http2.Client;

internal readonly record struct StreamBodyReadComplete(int StreamId, int BytesRead);
internal readonly record struct StreamBodyReadFailed(int StreamId, Exception Reason);

internal sealed class Http2ClientSessionManager
{
    private readonly Http2ClientEncoderOptions _encoderOptions;
    private readonly Http2ClientDecoderOptions _decoderOptions;
    private readonly TurboClientOptions _options;
    private readonly IClientStageOperations _ops;

    private readonly StreamTracker _tracker;
    private readonly FlowController _flow;
    private readonly StackStreamStatePool<StreamState> _statePool;
    private readonly FrameDecoder _frameDecoder;
    private readonly Http2ClientDecoder _responseDecoder;
    private readonly Http2ClientEncoder _requestEncoder;
    private readonly Dictionary<int, HttpRequestMessage> _correlationMap = new();

    private readonly Dictionary<int, StreamState> _streams = new();
    private readonly Dictionary<int, Stream> _activeBodyStreams = new();
    private readonly Dictionary<int, IMemoryOwner<byte>> _activeBodyBuffers = new();

    private bool _prefaceSent;
    private bool _awaitingPingAck;
    private long _pingSentTimestamp;

    private static readonly byte[] RttPingPayload = "RTTPROBE"u8.ToArray();

    public bool CanOpenStream => _tracker.CanOpenStream();
    public bool GoAwayReceived => _flow.GoAwayReceived;
    public int GoAwayLastStreamId { get; private set; }
    public bool HasInFlightRequests => _correlationMap.Count > 0 || _streams.Count > 0;
    public bool HasActiveStreams => _streams.Count > 0;
    public RequestEndpoint Endpoint { get; private set; }

    /// <summary>TEST ONLY: latest measured min-RTT, or zero if scaling disabled / no sample.</summary>
    internal TimeSpan MinRttForTest => _flow.MinRtt;

    /// <summary>True if the PING carries the measurement sentinel payload.</summary>
    internal static bool IsRttPing(PingFrame ping) =>
        ping.Data.Span.SequenceEqual(RttPingPayload);

    public Http2ClientSessionManager(
        TurboClientOptions options,
        IClientStageOperations ops,
        TimeProvider? timeProvider = null)
    {
        _encoderOptions = options.ToHttp2EncoderOptions();
        _decoderOptions = options.ToHttp2DecoderOptions();
        _options = options;
        _ops = ops;
        var clock = timeProvider ?? TimeProvider.System;
        _tracker = new StreamTracker(1, _decoderOptions.MaxConcurrentStreams);

        WindowScaler? scaler = null;
        if (_decoderOptions.EnableAdaptiveWindowScaling)
        {
            scaler = new WindowScaler(
                _decoderOptions.MaxStreamWindowSize,
                _decoderOptions.WindowScaleThresholdMultiplier);
        }

        _flow = new FlowController(
            _decoderOptions.InitialConnectionWindowSize,
            _decoderOptions.InitialStreamWindowSize,
            scaler,
            clock);
        // Outgoing frame size starts at the RFC 9113 default (16,384) and is raised only when the
        // server advertises a larger SETTINGS_MAX_FRAME_SIZE. The client's own MaxFrameSize option
        // is a receive-side advertisement (sent in the preface), not a send-side limit.
        _requestEncoder = new Http2ClientEncoder(useHuffman: true);
        var poolCapacity = Math.Min(
            _tracker.MaxConcurrentStreams > 0 ? _tracker.MaxConcurrentStreams : 100,
            1000);
        _statePool = new StackStreamStatePool<StreamState>(poolCapacity, () => new StreamState());
        _responseDecoder = new Http2ClientDecoder(_decoderOptions.MaxHeaderSize, _decoderOptions.MaxHeaderListSize);
        _responseDecoder.SetMaxAllowedTableSize(_encoderOptions.HeaderTableSize);
        // RFC 9113 §4.2: enforce the MAX_FRAME_SIZE we advertise in the preface on inbound frames.
        _frameDecoder = new FrameDecoder(_encoderOptions.MaxFrameSize);
    }

    public TransportData? TryBuildPreface()
    {
        if (_decoderOptions.InitialConnectionWindowSize <= 0 || _prefaceSent)
        {
            return null;
        }

        _prefaceSent = true;
        var (prefaceOwner, prefaceLength) = PrefaceBuilder.Build(
            _decoderOptions.InitialStreamWindowSize,
            _decoderOptions.InitialConnectionWindowSize,
            _encoderOptions.HeaderTableSize,
            _encoderOptions.MaxFrameSize);
        var prefaceBuf = TransportBuffer.Rent(prefaceLength);
        prefaceOwner.Memory.Span[..prefaceLength].CopyTo(prefaceBuf.FullMemory.Span);
        prefaceOwner.Dispose();
        prefaceBuf.Length = prefaceLength;
        return TransportData.Rent(prefaceBuf);
    }

    public void EncodeRequest(HttpRequestMessage request)
    {
        var streamId = _tracker.AllocateStreamId();

        if (GoAwayReceived)
        {
            Tracing.For("Protocol").Warning(this,
                "HTTP/2: RFC 9113 §6.8 - GOAWAY received; dropping new request (stream {0})", streamId);
            request.Fail(new HttpRequestException("HTTP/2 GOAWAY received."));
            return;
        }

        var endpoint = request.RequestUri is not null
            ? RequestEndpoint.FromRequest(request)
            : RequestEndpoint.Default;

        if (Endpoint == default && endpoint != default)
        {
            Endpoint = endpoint;
            var transportOptions = OptionsFactory.Build(Endpoint, _options);
            _ops.OnOutbound(new ConnectTransport(transportOptions));

            var preface = TryBuildPreface();
            if (preface is not null)
            {
                _ops.OnOutbound(preface);
            }
        }

        _correlationMap.TryAdd(streamId, request);

        if (request.RequestUri is null)
        {
            _tracker.OnStreamOpened(streamId);
            return;
        }

        var frames = _requestEncoder.Encode(request, streamId);

        if (frames.Count == 0)
        {
            return;
        }

        if (frames[0] is HeadersFrame headersFrame)
        {
            _tracker.OnStreamOpened(headersFrame.StreamId);
            _flow.InitStreamSendWindow(headersFrame.StreamId);
        }

        var totalSize = 0;
        for (var i = 0; i < frames.Count; i++)
        {
            totalSize += frames[i].SerializedSize;
        }

        var buf = TransportBuffer.Rent(totalSize);
        var span = buf.FullMemory.Span;
        for (var i = 0; i < frames.Count; i++)
        {
            frames[i].WriteTo(ref span);
        }

        buf.Length = totalSize;
        _ops.OnOutbound(TransportData.Rent(buf));

        if (request.Content is null)
        {
            return;
        }

        if (!_streams.TryGetValue(streamId, out var state))
        {
            state = _statePool.Rent();
            _streams[streamId] = state;
        }

        var contentLength = request.Content?.Headers.ContentLength;
        var bodyStream = request.Content?.ReadAsStream();

        // Fast path A: MemoryStream with an accessible backing buffer - slice directly into DATA
        // frames without allocating a MemoryPool chunk per read. The backing byte[] is kept alive
        // by HttpRequestMessage.Content for the duration of the request, so referencing its memory
        // here is safe. TryGetBuffer succeeds only for streams created with a publicly visible
        // buffer (e.g. new MemoryStream(), new MemoryStream(capacity)) - non-visible streams
        // (ByteArrayContent internal, MemoryStream(buf, false)) fall through to the slow path.
        if (bodyStream is MemoryStream ms && ms.TryGetBuffer(out var segment))
        {
            var pos = (int)ms.Position;
            var available = segment.Count - pos;
            if (available > 0)
            {
                EmitBodyDirect(streamId, state, segment.AsMemory(pos, available));
                return;
            }
        }

        // Fast path B: Content with a known length within the buffer threshold - copy body
        // directly into an ArrayPool-rented buffer via CopyTo, then emit frames without spinning
        // up the async encoder pipeline (no background Task, no actor messages, no per-chunk
        // MemoryPool.Rent). Handles ByteArrayContent, StringContent, ReadOnlyMemoryContent and
        // any other sync-serializable content. Falls through if the content does not support
        // synchronous serialization (CopyTo throws NotSupportedException).
        if (contentLength is > 0 and { } knownLength
            && knownLength <= _options.Http2.MaxBufferedRequestBodySize
            && TrySerializeBodyDirect(request.Content!, streamId, state, (int)knownLength))
        {
            return;
        }

        state.MarkBodyDrainActive();
        StartStreamBodyDrain(streamId, bodyStream!, contentLength);
    }

    private void EmitBodyDirect(int streamId, StreamState state, Memory<byte> body)
    {
        var maxFrame = _requestEncoder.MaxFrameSize;
        var window = (int)Math.Min(_flow.GetSendWindow(streamId), int.MaxValue);
        var sent = 0;

        while (sent < body.Length && window > 0)
        {
            var chunkLen = Math.Min(Math.Min(maxFrame, window), body.Length - sent);
            var endStream = sent + chunkLen >= body.Length;
            EmitFrame(new DataFrame(streamId, body.Slice(sent, chunkLen), endStream));
            _flow.OnDataSent(streamId, chunkLen);
            window -= chunkLen;
            sent += chunkLen;
        }

        if (sent >= body.Length)
        {
            // All data sent inline - mark complete and release stream state.
            CloseStream(streamId);

            if (state.IsRemoteClosed)
            {
                _streams.Remove(streamId);
                state.Reset();
                _statePool.Return(state);
            }

            return;
        }

        // Window exhausted before all data sent: buffer the remainder.
        // A copy into a pooled buffer is required here because the window-drain path
        // (DrainOutboundBuffer) expects IMemoryOwner<byte>-backed chunks.
        var remaining = body.Length - sent;
        var owner = MemoryPool<byte>.Shared.Rent(remaining);
        body.Slice(sent, remaining).CopyTo(owner.Memory);
        state.EnqueueBodyChunk(new StreamBodyChunk(owner, remaining));
        state.MarkBodyDrainComplete();
    }

    private bool TrySerializeBodyDirect(HttpContent content, int streamId, StreamState state, int bodyLength)
    {
        var pool = ArrayPool<byte>.Shared;
        var bodyArray = pool.Rent(bodyLength);
        try
        {
            using var ms = new MemoryStream(bodyArray, 0, bodyLength, writable: true);
            content.CopyTo(ms, null, CancellationToken.None);
        }
        catch (NotSupportedException)
        {
            // Content does not support synchronous serialization (CopyTo delegates to the
            // protected SerializeToStream which throws NotSupportedException for async-only
            // content). Fall back to the async encoder pipeline.
            pool.Return(bodyArray);
            return false;
        }

        EmitBodyDirect(streamId, state, new Memory<byte>(bodyArray, 0, bodyLength));

        // The array may be returned now: EmitBodyDirect copies all data into TransportBuffers
        // (via EmitFrame → DataFrame.WriteTo) or into a MemoryPool-owned copy for the
        // window-exhausted path before returning, so bodyArray is no longer referenced.
        pool.Return(bodyArray);
        return true;
    }

    public IReadOnlyList<Http2Frame> DecodeFrames(TransportBuffer buffer)
    {
        return _frameDecoder.Decode(buffer);
    }

    public void ProcessFrame(Http2Frame frame)
    {
        switch (frame)
        {
            case SettingsFrame settings:
                HandleSettings(settings);
                break;

            case DataFrame data:
                ProcessDataFrame(data);
                break;

            case HeadersFrame headers:
                HandleHeaders(headers);
                break;

            case ContinuationFrame cont:
                HandleContinuation(cont);
                break;

            case RstStreamFrame rst:
                HandleRstStream(rst);
                break;

            case WindowUpdateFrame win:
                HandleWindowUpdate(win);
                break;

            case PingFrame ping:
                HandlePing(ping);
                break;

            case GoAwayFrame goAway:
                HandleGoAway(goAway);
                break;
        }
    }

    public void SendKeepAlivePing()
    {
        if (_awaitingPingAck)
        {
            return;
        }

        _awaitingPingAck = true;
        _pingSentTimestamp = Environment.TickCount64;
        var data = BitConverter.GetBytes(_pingSentTimestamp);
        EmitFrame(new PingFrame(data, isAck: false));
    }

    private void MaybeSendMeasurementPing()
    {
        if (_flow.ShouldSendMeasurementPing())
        {
            _flow.OnMeasurementPingSent();
            EmitFrame(new PingFrame(RttPingPayload, isAck: false));
        }
    }

    public bool IsKeepAliveTimedOut(TimeSpan timeout)
    {
        if (!_awaitingPingAck)
        {
            return false;
        }

        var elapsed = Environment.TickCount64 - _pingSentTimestamp;
        return elapsed >= (long)timeout.TotalMilliseconds;
    }

    public bool TryCancelStream(HttpRequestMessage request)
    {
        var streamId = -1;
        foreach (var (id, req) in _correlationMap)
        {
            if (ReferenceEquals(req, request))
            {
                streamId = id;
                break;
            }
        }

        if (streamId < 0)
        {
            return false;
        }

        EmitFrame(new RstStreamFrame(streamId, Http2ErrorCode.Cancel));
        _correlationMap.Remove(streamId);
        request.Fail(new OperationCanceledException("Request cancelled by caller."));
        CloseStream(streamId);

        return true;
    }

    public IReadOnlyDictionary<int, HttpRequestMessage> GetCorrelationMap()
    {
        return _correlationMap;
    }

    public bool HasReceivedHeaders(int streamId)
    {
        return _streams.GetValueOrDefault(streamId)?.HasResponse ?? false;
    }

    public void ReleaseAllStreamState()
    {
        foreach (var (_, state) in _streams)
        {
            state.Reset();
            _statePool.Return(state);
        }

        _streams.Clear();
        _correlationMap.Clear();
    }

    public void ResetConnectionState()
    {
        _tracker.Reset();
        _flow.Reset(_decoderOptions.InitialConnectionWindowSize, _decoderOptions.InitialStreamWindowSize);
        _requestEncoder.ResetHpack();
        _responseDecoder.ResetHpack();
        _prefaceSent = false;
    }

    public void Cleanup()
    {
        foreach (var (streamId, state) in _streams)
        {
            state.AbortBody();
            CleanupBodyDrain(streamId);
        }

        ReleaseAllStreamState();
    }

    private void EmitDataFrames(int streamId, ReadOnlyMemory<byte> data)
    {
        // Split DATA frames by the server's advertised MAX_FRAME_SIZE (tracked by the encoder),
        // not the client's own receive-side option.
        var maxFrame = _requestEncoder.MaxFrameSize;
        var remaining = data;
        while (remaining.Length > maxFrame)
        {
            EmitFrame(new DataFrame(streamId, remaining[..maxFrame], endStream: false));
            remaining = remaining[maxFrame..];
        }

        if (!remaining.IsEmpty)
        {
            EmitFrame(new DataFrame(streamId, remaining, endStream: false));
        }
    }

    private void EmitFrame(Http2Frame frame)
    {
        var buf = TransportBuffer.Rent(frame.SerializedSize);
        var span = buf.FullMemory.Span;
        frame.WriteTo(ref span);
        buf.Length = frame.SerializedSize;
        _ops.OnOutbound(TransportData.Rent(buf));
    }

    private void HandleSettings(SettingsFrame frame)
    {
        var result = _flow.OnRemoteSettings(frame);

        if (result.AckFrame is null)
        {
            return;
        }

        if (result.MaxConcurrentStreamsChange is { } maxStreams)
        {
            _tracker.SetMaxConcurrentStreams(maxStreams);
        }

        _requestEncoder.ApplyServerSettings(frame.Parameters);
        EmitFrame(result.AckFrame);
    }

    private void ProcessDataFrame(DataFrame data)
    {
        var result = _flow.OnInboundData(data.StreamId, data.Data.Length);

        if (result.IsConnectionViolation)
        {
            Tracing.For("Protocol").Info(this,
                "HTTP/2: RFC 9113 §6.9 - connection flow control window exceeded. Triggering reconnect");
            _ops.OnOutbound(new DisconnectTransport(DisconnectReason.Error));
            return;
        }

        if (result.IsStreamViolation)
        {
            Tracing.For("Protocol").Info(this,
                "HTTP/2: RFC 9113 §6.9 - stream {0} flow control window exceeded. Triggering reconnect", data.StreamId);
            _ops.OnOutbound(new DisconnectTransport(DisconnectReason.Error));
            return;
        }

        if (result.ConnectionWindowUpdate is { } connUpdate)
        {
            EmitFrame(new WindowUpdateFrame(connUpdate.StreamId, connUpdate.Increment));
        }

        if (result.StreamWindowUpdate is { } streamUpdate)
        {
            EmitFrame(new WindowUpdateFrame(streamUpdate.StreamId, streamUpdate.Increment));
        }

        MaybeSendMeasurementPing();

        HandleData(data);

        if (data.EndStream)
        {
            var hasActiveBodyEncoder = _streams.TryGetValue(data.StreamId, out var state)
                                       && state is { HasBodyDrain: true, IsBodyDrainComplete: false };
            if (!hasActiveBodyEncoder)
            {
                CloseStream(data.StreamId);
            }
        }
    }

    private void HandlePing(PingFrame ping)
    {
        if (ping.IsAck)
        {
            if (IsRttPing(ping))
            {
                _flow.OnMeasurementPingAck();
                return;
            }

            _awaitingPingAck = false;
            return;
        }

        var ack = _flow.OnPing(ping);
        if (ack is not null)
        {
            EmitFrame(ack);
        }
    }

    private void HandleGoAway(GoAwayFrame goAway)
    {
        _flow.OnGoAway();
        GoAwayLastStreamId = goAway.LastStreamId;
        Tracing.For("Protocol").Info(this,
            "HTTP/2: GOAWAY received from {0} - LastStreamId={1}, ErrorCode={2}. Reconnecting", Endpoint.Host,
            goAway.LastStreamId, goAway.ErrorCode);
    }

    private void HandleRstStream(RstStreamFrame rst)
    {
        if (_correlationMap.Remove(rst.StreamId, out var request))
        {
            request.Fail(new HttpRequestException(
                string.Concat("HTTP/2 stream ", rst.StreamId.ToString(), " was reset by the server (error code ",
                    rst.ErrorCode.ToString(), ").")));
        }

        CloseStream(rst.StreamId);
    }

    private void CloseStream(int streamId)
    {
        if (_streams.TryGetValue(streamId, out var state) && state.HasBodyReader)
        {
            state.AbortBody();
        }

        CleanupBodyDrain(streamId);
        _tracker.OnStreamClosed(streamId);
        _flow.RemoveStreamSendWindow(streamId);

        var signal = _flow.OnStreamClosed(streamId);
        if (signal is { } windowUpdate)
        {
            EmitFrame(new WindowUpdateFrame(windowUpdate.StreamId, windowUpdate.Increment));
        }
    }

    private void HandleHeaders(HeadersFrame frame)
    {
        if (!_streams.TryGetValue(frame.StreamId, out var state))
        {
            state = _statePool.Rent();
            _streams[frame.StreamId] = state;
        }

        state.AppendHeader(frame.HeaderBlockFragment.Span, _decoderOptions.MaxHeaderListSize);

        if (!frame.EndHeaders)
        {
            return;
        }

        DecodeHeaders(frame.StreamId, frame.EndStream);
    }

    private void HandleContinuation(ContinuationFrame frame)
    {
        if (!_streams.TryGetValue(frame.StreamId, out var state))
        {
            Tracing.For("Protocol").Warning(this, "HTTP/2: Received CONTINUATION for unknown stream {0} - dropping",
                frame.StreamId);
            return;
        }

        state.AppendHeader(frame.HeaderBlockFragment.Span, _decoderOptions.MaxHeaderListSize);

        if (frame.EndHeaders)
        {
            DecodeHeaders(frame.StreamId, false);
        }
    }

    private void HandleData(DataFrame frame)
    {
        if (!_streams.TryGetValue(frame.StreamId, out var state))
        {
            Tracing.For("Protocol").Warning(this, "HTTP/2: Received DATA for unknown stream {0} - dropping",
                frame.StreamId);
            return;
        }

        if (!state.HasBodyReader)
        {
            Tracing.For("Protocol").Warning(this, "HTTP/2: Received DATA before HEADERS on stream {0} - dropping",
                frame.StreamId);
            return;
        }

        state.FeedBody(frame.Data.Span, frame.EndStream);

        if (frame.EndStream)
        {
            state.DetachBodyReader();
            state.MarkRemoteClosed();

            if (!state.HasBodyDrain || state.IsBodyDrainComplete)
            {
                _streams.Remove(frame.StreamId);
                state.Reset();
                _statePool.Return(state);
            }
        }
    }

    private void DecodeHeaders(int streamId, bool endStream)
    {
        if (!_streams.TryGetValue(streamId, out var state))
        {
            Tracing.For("Protocol").Warning(this, "HTTP/2: DecodeHeaders called for unknown stream {0} - dropping",
                streamId);
            return;
        }

        if (state.HasResponse)
        {
            _responseDecoder.DecodeTrailers(state);
            state.ClearHeaderBuffer();
            if (endStream)
            {
                _streams.Remove(streamId);
                state.DetachBodyReader();
                state.Reset();
                _statePool.Return(state);
            }

            return;
        }

        if (endStream)
        {
            var response = _responseDecoder.DecodeHeaders(streamId, true, state);
            state.ClearHeaderBuffer();
            if (response is null)
            {
                return;
            }

            if ((int)response.StatusCode < 200)
            {
                if (_correlationMap.TryGetValue(streamId, out var interimReq))
                {
                    response.RequestMessage = interimReq;
                }

                _ops.OnResponse(response);

                if (endStream)
                {
                    _correlationMap.Remove(streamId);
                    _streams.Remove(streamId);
                    state.Reset();
                    _statePool.Return(state);
                }

                return;
            }

            if (_correlationMap.Remove(streamId, out var req))
            {
                response.RequestMessage = req;
            }

            var partialContentResult = PartialContentValidator.Validate(response);
            if (!partialContentResult.IsValid)
            {
                Tracing.For("Protocol").Warning(this, "HTTP/2: {0}", partialContentResult.ErrorMessage!);
            }

            _ops.OnResponse(response);

            _streams.Remove(streamId);
            state.Reset();
            _statePool.Return(state);
            return;
        }

        var streamingResponse = _responseDecoder.DecodeHeadersForStreaming(streamId, state);
        state.ClearHeaderBuffer();

        if ((int)streamingResponse.StatusCode < 200)
        {
            return;
        }

        var queued = new QueuedBodyReader(capacity: 8);
        queued.Reset();
        state.InitBodyReader(queued);
        var bodyStream = state.GetBodyStream();
        streamingResponse.Content = new StreamContent(bodyStream);
        state.ApplyContentHeadersTo(streamingResponse.Content);

        if (_correlationMap.Remove(streamId, out var request))
        {
            streamingResponse.RequestMessage = request;
        }

        var partialResult = PartialContentValidator.Validate(streamingResponse);
        if (!partialResult.IsValid)
        {
            Tracing.For("Protocol").Warning(this, "HTTP/2: {0}", partialResult.ErrorMessage!);
        }

        _ops.OnResponse(streamingResponse);
    }

    public void OnBodyMessage(object msg)
    {
        switch (msg)
        {
            case StreamBodyReadComplete read:
                HandleStreamBodyRead(read);
                break;

            case StreamBodyReadFailed failed:
                Tracing.For("Protocol").Warning(this,
                    "HTTP/2: Body drain failed for stream {0}: {1}", failed.StreamId, failed.Reason.Message);
                EmitFrame(new RstStreamFrame(failed.StreamId, Http2ErrorCode.InternalError));
                CleanupBodyDrain(failed.StreamId);
                CloseStream(failed.StreamId);
                break;
        }
    }

    private void HandleStreamBodyRead(StreamBodyReadComplete read)
    {
        if (!_streams.TryGetValue(read.StreamId, out var state))
        {
            CleanupBodyDrain(read.StreamId);
            return;
        }

        if (read.BytesRead == 0)
        {
            EmitFrame(new DataFrame(read.StreamId, ReadOnlyMemory<byte>.Empty, endStream: true));
            state.MarkBodyDrainComplete();
            CleanupBodyDrain(read.StreamId);

            if (state.IsRemoteClosed)
            {
                _streams.Remove(read.StreamId);
                state.Reset();
                _statePool.Return(state);
            }

            return;
        }

        if (!_activeBodyBuffers.TryGetValue(read.StreamId, out var buffer))
        {
            CleanupBodyDrain(read.StreamId);
            return;
        }

        var data = buffer.Memory[..read.BytesRead];
        var window = (int)Math.Min(_flow.GetSendWindow(read.StreamId), int.MaxValue);

        if (window >= read.BytesRead)
        {
            EmitDataFrames(read.StreamId, data);
            _flow.OnDataSent(read.StreamId, read.BytesRead);
            ReadNextBodyChunk(read.StreamId);
        }
        else if (window > 0)
        {
            EmitDataFrames(read.StreamId, data[..window]);
            _flow.OnDataSent(read.StreamId, window);

            var remaining = read.BytesRead - window;
            var owner = MemoryPool<byte>.Shared.Rent(remaining);
            data[window..].CopyTo(owner.Memory);
            state.EnqueueBodyChunk(new StreamBodyChunk(owner, remaining));
        }
        else
        {
            var owner = MemoryPool<byte>.Shared.Rent(read.BytesRead);
            data[..read.BytesRead].CopyTo(owner.Memory);
            state.EnqueueBodyChunk(new StreamBodyChunk(owner, read.BytesRead));
        }
    }

    private void DrainOutboundBuffer(int streamId)
    {
        if (!_streams.TryGetValue(streamId, out var state) || !state.HasPendingOutbound)
        {
            return;
        }

        while (state.PeekBodyChunk() is not null)
        {
            var window = (int)Math.Min(_flow.GetSendWindow(streamId), int.MaxValue);
            if (window <= 0)
            {
                break;
            }

            state.TryDequeueBodyChunk(out var chunk);

            if (window >= chunk!.Length)
            {
                EmitDataFrames(streamId, chunk.Data);
                _flow.OnDataSent(streamId, chunk.Length);
                chunk.Owner.Dispose();
            }
            else
            {
                EmitDataFrames(streamId, chunk.Data[..window]);
                _flow.OnDataSent(streamId, window);
                state.PrependBodyChunk(chunk with { Offset = chunk.Offset + window, Length = chunk.Length - window });
                break;
            }
        }

        if (state is { HasPendingOutbound: false, IsBodyDrainComplete: true })
        {
            EmitFrame(new DataFrame(streamId, ReadOnlyMemory<byte>.Empty, endStream: true));
            CloseStream(streamId);

            if (state.IsRemoteClosed)
            {
                _streams.Remove(streamId);
                state.Reset();
                _statePool.Return(state);
            }
        }
        else if (!state.HasPendingOutbound && state.HasBodyDrain && !state.IsBodyDrainComplete)
        {
            ReadNextBodyChunk(streamId);
        }
    }

    private void HandleWindowUpdate(WindowUpdateFrame frame)
    {
        _flow.OnSendWindowUpdate(frame.StreamId, frame.Increment);

        if (frame.StreamId == 0)
        {
            foreach (var streamId in _streams.Keys.ToList())
            {
                DrainOutboundBuffer(streamId);
            }
        }
        else
        {
            DrainOutboundBuffer(frame.StreamId);
        }
    }

    private void StartStreamBodyDrain(int streamId, Stream bodyStream, long? contentLength = null)
    {
        _activeBodyStreams[streamId] = bodyStream;
        var maxSize = Math.Min(_options.RequestBodyChunkSize, _requestEncoder.MaxFrameSize);
        var bufferSize = contentLength is > 0 and <= int.MaxValue
            ? (int)Math.Min(contentLength.Value, maxSize)
            : maxSize;
        var buffer = MemoryPool<byte>.Shared.Rent(Math.Max(bufferSize, 256));
        _activeBodyBuffers[streamId] = buffer;
        ReadNextBodyChunk(streamId);
    }

    private void ReadNextBodyChunk(int streamId)
    {
        if (!_activeBodyStreams.TryGetValue(streamId, out var stream) ||
            !_activeBodyBuffers.TryGetValue(streamId, out var buffer))
        {
            return;
        }

        stream.ReadAsync(buffer.Memory).AsTask().PipeTo(
            _ops.StageActor,
            success: bytesRead => new StreamBodyReadComplete(streamId, bytesRead),
            failure: ex => new StreamBodyReadFailed(streamId, ex));
    }

    private void CleanupBodyDrain(int streamId)
    {
        if (_activeBodyBuffers.Remove(streamId, out var buffer))
        {
            buffer.Dispose();
        }

        _activeBodyStreams.Remove(streamId);
    }
}