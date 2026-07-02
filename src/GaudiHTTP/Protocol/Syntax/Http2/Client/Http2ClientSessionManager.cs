using System.Buffers;
using System.Net;
using Akka.Actor;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Internal;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Protocol.Syntax.Http2.Options;
using GaudiHTTP.Pooling;
using GaudiHTTP.Streams.Stages.Client;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Syntax.Http2.Client;

internal sealed class Http2ClientSessionManager : IBodyDrainTarget
{
    internal sealed record AbandonedResponseBody(int StreamId);
    private readonly Http2ClientEncoderOptions _encoderOptions;
    private readonly Http2ClientDecoderOptions _decoderOptions;
    private readonly GaudiClientOptions _options;
    private readonly IClientStageOperations _ops;

    private readonly StreamTracker _tracker;
    private readonly FlowController _flow;
    private readonly FrameDecoder _frameDecoder;
    private readonly Http2ClientDecoder _responseDecoder;
    private readonly Http2ClientEncoder _requestEncoder;
    private readonly Dictionary<int, HttpRequestMessage> _correlationMap = new();

    private readonly Dictionary<int, StreamState> _streams = new();
    private readonly Dictionary<int, HttpContent> _drainContentOwners = new();
    private readonly CancellationTokenSource _connectionCts = new();
    private FlowControlledBodyPump? _scheduler;
    private readonly int _maxBufferedResponseBodySize;

    private bool _prefaceSent;
    private bool _awaitingPingAck;
    private long _pingSentTimestamp;

    private static readonly byte[] RttPingPayload = "RTTPROBE"u8.ToArray();

    public bool CanOpenStream => _tracker.CanOpenStream();
    public bool GoAwayReceived => _flow.GoAwayReceived;
    public int GoAwayLastStreamId { get; private set; }
    public bool GoAwayWasGraceful { get; private set; }
    public bool HasInFlightRequests => _correlationMap.Count > 0 || _streams.Count > 0;
    public bool HasActiveStreams => _streams.Count > 0;
    public RequestEndpoint Endpoint { get; private set; }

    /// <summary>TEST ONLY: latest measured min-RTT, or zero if scaling disabled / no sample.</summary>
    internal TimeSpan MinRttForTest => _flow.MinRtt;

    /// <summary>True if the PING carries the measurement sentinel payload.</summary>
    internal static bool IsRttPing(PingFrame ping) =>
        ping.Data.Span.SequenceEqual(RttPingPayload);

    public Http2ClientSessionManager(
        GaudiClientOptions options,
        IClientStageOperations ops,
        TimeProvider? timeProvider = null)
    {
        _encoderOptions = options.ToHttp2EncoderOptions();
        _decoderOptions = options.ToHttp2DecoderOptions();
        _options = options;
        _ops = ops;
        _maxBufferedResponseBodySize = options.ResolveMaxBufferedResponseBodySize(options.Http2);
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
        _responseDecoder = new Http2ClientDecoder(_decoderOptions.MaxHeaderSize, _decoderOptions.MaxHeaderListSize);
        _responseDecoder.SetMaxAllowedTableSize(_encoderOptions.HeaderTableSize);
        // RFC 9113 §4.2: enforce the MAX_FRAME_SIZE we advertise in the preface on inbound frames.
        _frameDecoder = new FrameDecoder(_encoderOptions.MaxFrameSize);
        _scheduler = new FlowControlledBodyPump(this, _flow, _connectionCts, _requestEncoder.MaxFrameSize, 256);
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
            _encoderOptions.MaxFrameSize,
            _decoderOptions.MaxHeaderListSize);
        var prefaceBuf = TransportBuffer.Rent(prefaceLength);
        prefaceOwner.Memory.Span[..prefaceLength].CopyTo(prefaceBuf.FullMemory.Span);
        prefaceOwner.Dispose();
        prefaceBuf.Length = prefaceLength;
        return TransportData.Rent(prefaceBuf);
    }

    public void EncodeRequest(HttpRequestMessage request)
    {
        if (!CanOpenStream)
        {
            Tracing.For("Protocol").Warning(this,
                "HTTP/2: EncodeRequest called at MaxConcurrentStreams limit (active={0}, max={1})",
                _tracker.ActiveStreamCount, _tracker.MaxConcurrentStreams);
        }

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
            state = ConnectionObjectPool.Instance.Rent(() => new StreamState());
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
        //
        // Guard: skip when the connection send window is exhausted. Under high concurrency
        // (512+ streams), streams that can't send inline buffer the ENTIRE body (~1 MB each)
        // in MemoryPool chunks. With 500+ streams this creates ~500 MB of Gen-2 garbage per
        // round, causing multi-second GC pauses that stall the Akka dispatcher. The async
        // drain path (StartStreamBodyDrain) reads in 64 KB chunks instead, keeping peak
        // memory at ~32 MB even at extreme concurrency.
        if (contentLength is > 0 and var knownLength
                                     && knownLength <= _options.ResolveMaxBufferedRequestBodySize(_options.Http2)
                                     && _flow.ConnectionSendWindow > 0
                                                     && TrySerializeBodyDirect(request.Content!, streamId, state,
                                                         (int)knownLength))
        {
            return;
        }

        if (contentLength == 0)
        {
            // Empty body: emit END_STREAM directly without involving the scheduler (spec invariant 7).
            ((IBodyDrainTarget)this).EmitDataFrames(streamId, default, endStream: true);
            CloseStream(streamId);
            return;
        }

        state.MarkBodyDrainActive();
        _drainContentOwners[streamId] = request.Content!;
        _scheduler!.Register(streamId, bodyStream!, request.Content?.Headers.ContentLength, request.GetCancellationToken());
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
                ReturnBodyReader(state);
                state.Dispose();
            }

            return;
        }

        // Window exhausted before all data sent: hand the remainder to the scheduler
        // which will emit it when the send window opens up via WINDOW_UPDATE.
        state.MarkBodyDrainActive();
        _scheduler!.Register(streamId, new MemoryStream(body[sent..].ToArray(), writable: false), null, CancellationToken.None);
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
        // Decode returns the decoder's reused frame list; the only caller
        // (Http2ClientStateMachine.OnInbound) iterates it synchronously within the same actor
        // message and never retains it across Decode calls.
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

    /// <summary>
    /// True if any in-flight request occupies a stream id at or below <paramref name="lastStreamId"/> —
    /// i.e. a stream the GOAWAY sender committed to finish (RFC 9113 §6.8). When present, a graceful
    /// GOAWAY is drained on the open connection; when absent there is nothing to wait for, so the
    /// connection is reconnected immediately to replay the discarded streams.
    /// </summary>
    public bool HasInFlightStreamsAtOrBelow(int lastStreamId)
    {
        foreach (var streamId in _correlationMap.Keys)
        {
            if (streamId <= lastStreamId)
            {
                return true;
            }
        }

        foreach (var streamId in _streams.Keys)
        {
            if (streamId <= lastStreamId)
            {
                return true;
            }
        }

        return false;
    }

    public bool HasReceivedHeaders(int streamId)
    {
        return _streams.GetValueOrDefault(streamId)?.HasResponse ?? false;
    }

    public void ReleaseAllStreamState()
    {
        foreach (var (_, state) in _streams)
        {
            ReturnBodyReader(state);
            state.Dispose();
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
        GoAwayWasGraceful = false;
    }

    public void Cleanup()
    {
        foreach (var (_, state) in _streams)
        {
            state.AbortBody();
        }

        _scheduler?.Cleanup();
        _drainContentOwners.Clear();
        ReleaseAllStreamState();
    }


    IActorRef IBodyDrainTarget.StageActor => _ops.StageActor;

    void IBodyDrainTarget.EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
    {
        const int headerSize = 9;
        var maxFrame = _requestEncoder.MaxFrameSize;

        // Empty body: emit a single empty DATA frame only to carry END_STREAM; otherwise nothing.
        if (data.IsEmpty)
        {
            if (!endStream)
            {
                return;
            }

            var emptyBuf = TransportBuffer.Rent(headerSize);
            DataFrame.WriteHeaderInPlace(emptyBuf.FullMemory.Span, 0, streamId, 0, endStream: true);
            emptyBuf.Length = headerSize;
            Tracing.For("Protocol").Trace(this, "HTTP/2: DATA out (stream={0}, len={1}, endStream={2})",
                streamId, 0, true);
            _ops.OnOutbound(TransportData.Rent(emptyBuf));
            return;
        }

        // Batch the whole body into ONE TransportBuffer with in-place frame headers (mirrors the
        // server's EmitBufferedDataFrames). The per-frame path used to rent a TransportBuffer and
        // hand out a separate outbound item per DATA frame, churning the pool on large uploads.
        var frameCount = (data.Length + maxFrame - 1) / maxFrame;
        var totalWireSize = data.Length + frameCount * headerSize;

        var buf = TransportBuffer.Rent(totalWireSize);
        var dest = buf.FullMemory.Span;
        var offset = 0;
        var remaining = data;

        while (remaining.Length > maxFrame)
        {
            DataFrame.WriteHeaderInPlace(dest, offset, streamId, maxFrame, endStream: false);
            remaining[..maxFrame].Span.CopyTo(dest[(offset + headerSize)..]);
            offset += headerSize + maxFrame;
            remaining = remaining[maxFrame..];
            Tracing.For("Protocol").Trace(this, "HTTP/2: DATA out (stream={0}, len={1}, endStream={2})",
                streamId, maxFrame, false);
        }

        var lastLen = remaining.Length;
        DataFrame.WriteHeaderInPlace(dest, offset, streamId, lastLen, endStream);
        remaining.Span.CopyTo(dest[(offset + headerSize)..]);
        offset += headerSize + lastLen;
        Tracing.For("Protocol").Trace(this, "HTTP/2: DATA out (stream={0}, len={1}, endStream={2})",
            streamId, lastLen, endStream);

        buf.Length = offset;
        _ops.OnOutbound(TransportData.Rent(buf));
    }

    void IBodyDrainTarget.OnDrainComplete(int streamId)
    {
        _drainContentOwners.Remove(streamId);

        if (_streams.TryGetValue(streamId, out var state))
        {
            state.MarkBodyDrainComplete();
        }

        CloseStream(streamId);

        if (state is { IsRemoteClosed: true })
        {
            _streams.Remove(streamId);
            ReturnBodyReader(state);
            state.Dispose();
        }
    }

    void IBodyDrainTarget.OnDrainFailed(int streamId, Exception reason)
    {
        _drainContentOwners.Remove(streamId);
        Tracing.For("Protocol").Warning(this,
            "HTTP/2: Body drain failed for stream {0}: {1}", streamId, reason.Message);
        EmitFrame(new RstStreamFrame(streamId, Http2ErrorCode.InternalError));
        CloseStream(streamId);
    }

    private void EmitFrame(Http2Frame frame)
    {
        if (frame is DataFrame d)
        {
            Tracing.For("Protocol").Trace(this, "HTTP/2: DATA out (stream={0}, len={1}, endStream={2})",
                d.StreamId, d.Data.Length, d.EndStream);
        }

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
        Tracing.For("Protocol").Trace(this, "HTTP/2: DATA in (stream={0}, len={1}, endStream={2})",
            data.StreamId, data.Data.Length, data.EndStream);

        var result = _flow.OnInboundData(data.StreamId, data.FlowControlledLength);

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
        GoAwayWasGraceful = goAway.ErrorCode == Http2ErrorCode.NoError;
        Tracing.For("Protocol").Info(this,
            "HTTP/2: GOAWAY received from {0} - LastStreamId={1}, ErrorCode={2}", Endpoint.Host,
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

        _scheduler?.Cancel(streamId);
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
            state = ConnectionObjectPool.Instance.Rent(() => new StreamState());
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
            var reader = state.TakeBodyReader();
            if (reader is BufferedBodyReader buffered)
            {
                DispatchBufferedResponse(frame.StreamId, state, buffered);
            }

            state.MarkRemoteClosed();

            if (!state.HasBodyDrain || state.IsBodyDrainComplete)
            {
                _streams.Remove(frame.StreamId);
                ReturnBodyReader(state);
                state.Dispose();
            }
        }
    }

    /// <summary>
    /// Completes a response whose body was buffered (Content-Length within the configured
    /// threshold): the body reader collected every DATA frame, so the final byte[] is materialized
    /// here — after completion, not at HEADERS time — because <see cref="BufferedBodyReader.AsStream"/>
    /// snapshots the received length at call time.
    /// </summary>
    private void DispatchBufferedResponse(int streamId, StreamState state, BufferedBodyReader buffered)
    {
        var response = state.GetResponse();
        response.Content = new StreamContent(buffered.AsStream());
        state.ApplyContentHeadersTo(response.Content);

        if (_correlationMap.Remove(streamId, out var request))
        {
            response.RequestMessage = request;
        }

        var partialResult = PartialContentValidator.Validate(response);
        if (!partialResult.IsValid)
        {
            Tracing.For("Protocol").Warning(this, "HTTP/2: {0}", partialResult.ErrorMessage!);
        }

        _ops.OnResponse(response);
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
                state.FeedBody([], endStream: true);
                var reader = state.TakeBodyReader();
                if (reader is BufferedBodyReader buffered)
                {
                    DispatchBufferedResponse(streamId, state, buffered);
                }

                _streams.Remove(streamId);
                ReturnBodyReader(state);
                state.Dispose();
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
                    ReturnBodyReader(state);
                    state.Dispose();
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
            ReturnBodyReader(state);
            state.Dispose();
            return;
        }

        var streamingResponse = _responseDecoder.DecodeHeadersForStreaming(streamId, state);
        state.ClearHeaderBuffer();

        if ((int)streamingResponse.StatusCode < 200)
        {
            return;
        }

        // Peek the correlated request without removing it yet: for the buffered path the
        // correlation entry must stay alive until DispatchBufferedResponse (so a mid-buffer
        // RST_STREAM/GOAWAY/disconnect still fails the caller's Task via the existing paths).
        _correlationMap.TryGetValue(streamId, out var pendingRequest);

        // RFC 9113 §8.1.1: a stream ending before the declared Content-Length is malformed.
        // HEAD/204/304 legitimately carry Content-Length without a body.
        var noBodyExpected = pendingRequest?.Method == HttpMethod.Head
                             || streamingResponse.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotModified;

        var contentLength = noBodyExpected ? null : state.PeekContentLength();
        if (!noBodyExpected && contentLength is > 0 and var n && n <= _maxBufferedResponseBodySize)
        {
            // Small, known-length body: collect every DATA frame and deliver the full body once
            // complete instead of paying QueuedBodyReader's channel overhead. OnResponse is
            // deferred to DispatchBufferedResponse (see HandleData / trailers branch above).
            var buffered = ConnectionObjectPool.Instance.Rent(static () => new BufferedBodyReader());
            buffered.Reset((int)n);
            state.InitBodyReader(buffered);
            return;
        }

        var queued = ConnectionObjectPool.Instance.Rent(() => new QueuedBodyReader(capacity: 8));
        state.InitBodyReader(queued);
        var stageActor = _ops.StageActor;
        var capturedStreamId = streamId;
        var bodyStream = queued.AsStream(onAbandoned: () =>
            stageActor.Tell(new AbandonedResponseBody(capturedStreamId)));
        streamingResponse.Content = new StreamContent(bodyStream);
        state.ApplyContentHeadersTo(streamingResponse.Content);

        _correlationMap.Remove(streamId);
        streamingResponse.RequestMessage = pendingRequest;

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
            case BodyReadContinue<int> dc:
                _scheduler?.HandleBodyReadContinue(dc.StreamId);
                break;

            case BodyReadComplete<int> read:
                _scheduler?.HandleReadComplete(read.StreamId, read.BytesRead);
                break;

            case BodyReadFailed<int> failed:
                _scheduler?.HandleReadFailed(failed.StreamId, failed.Reason);
                break;

            case AbandonedResponseBody abandoned:
                OnResponseBodyAbandoned(abandoned.StreamId);
                break;
        }
    }

    private void OnResponseBodyAbandoned(int streamId)
    {
        if (!_streams.TryGetValue(streamId, out var state))
        {
            return;
        }

        Tracing.For("Protocol").Debug(this,
            "HTTP/2: response body abandoned (stream={0}) — sending RST_STREAM", streamId);

        state.AbortBody();
        EmitFrame(new RstStreamFrame(streamId, Http2ErrorCode.NoError));
        _streams.Remove(streamId);
        ReturnBodyReader(state);
        state.Dispose();

        if (!_tracker.OnStreamClosed(streamId))
        {
            return;
        }

        _flow.RemoveStreamSendWindow(streamId);
        var signal = _flow.OnStreamClosed(streamId);
        if (signal is { } windowUpdate)
        {
            EmitFrame(new WindowUpdateFrame(windowUpdate.StreamId, windowUpdate.Increment));
        }
    }

    private void HandleWindowUpdate(WindowUpdateFrame frame)
    {
        _flow.OnSendWindowUpdate(frame.StreamId, frame.Increment);
        _scheduler?.OnWindowUpdate(frame.StreamId);
    }

    private void ReturnBodyReader(StreamState state)
    {
        var reader = state.TakeBodyReader();
        if (reader is QueuedBodyReader queued)
        {
            queued.Dispose();
        }
        else
        {
            reader?.Dispose();
        }
    }
}