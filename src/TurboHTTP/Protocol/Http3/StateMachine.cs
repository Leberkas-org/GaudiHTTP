using System.Buffers;
using System.Net;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3.Qpack;
using TurboHTTP.Streams.Stages;

namespace TurboHTTP.Protocol.Http3;

/// <summary>
/// Encapsulates all HTTP/3 connection protocol logic — frame decoding, request encoding,
/// response assembly, QPACK feedback, SETTINGS, GOAWAY, push control, stream lifecycle,
/// idle timeout, and reconnection.
/// Calls back into <see cref="IStageOperations"/> for responses, outbound items, and warnings.
/// Mirrors the HTTP/2 <see cref="Http2.StateMachine"/> pattern.
/// </summary>
public sealed class StateMachine : IDisposable
{
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// HTTP methods that are safe for 0-RTT early data per RFC 9114 §A.1.
    /// </summary>
    private static readonly HashSet<HttpMethod> IdempotentMethods =
    [
        HttpMethod.Get,
        HttpMethod.Head,
        HttpMethod.Options,
        HttpMethod.Trace,
        HttpMethod.Delete,
    ];

    private readonly Http3ConnectionConfig _config;
    private readonly IStageOperations _ops;

    // Protocol-layer components
    private readonly FrameDecoder _frameDecoder = new();
    private readonly RequestEncoder _requestEncoder;
    private readonly QpackDecoder _qpackDecoder = new();
    private readonly QpackInstructionDecoder _instructionDecoder = new();

    // Per-stream response assembly and correlation (keyed by QUIC stream ID)
    private readonly Dictionary<long, StreamState> _streams = new();
    private readonly Dictionary<long, HttpRequestMessage> _correlationMap = new();
    private readonly Stack<StreamState> _statePool = new();
    private const int MaxPoolSize = 16;

    // Reconnection
    private readonly List<Http3Frame> _reconnectBuffer = [];
    private int _reconnectAttempts;

    // Preface tracking
    private bool _controlPrefaceSent;
    private bool _qpackEncoderPrefaceSent;

    /// <summary>Whether a new request can be accepted (no GOAWAY + not reconnecting + concurrency budget).</summary>
    public bool CanAcceptRequest => !Connection.GoAwayReceived && !IsReconnecting && Tracker.CanOpenStream();

    /// <summary>Whether the connection is currently in the reconnection phase.</summary>
    public bool IsReconnecting { get; private set; }

    /// <summary>Number of frames buffered for replay on reconnection.</summary>
    public int ReconnectBufferCount => _reconnectBuffer.Count;

    /// <summary>Whether a response was produced during the most recent ProcessFrame call.</summary>
    public bool ResponseProduced { get; private set; }

    /// <summary>Whether there are in-flight requests awaiting responses.</summary>
    public bool HasInFlightRequests => _correlationMap.Count > 0 || _streams.Count > 0;

    /// <summary>The current connection endpoint.</summary>
    public RequestEndpoint Endpoint { get; private set; }

    /// <summary>The underlying stream tracker for stream ID allocation and concurrency.</summary>
    internal StreamTracker Tracker { get; }

    /// <summary>The underlying connection state for idle timeout and settings inspection.</summary>
    internal ConnectionState Connection { get; }

    public StateMachine(Http3ConnectionConfig config, IStageOperations ops)
    {
        _config = config;
        _ops = ops;
        // RFC 9204 §3.2.3: the encoder MUST NOT use the dynamic table until the
        // peer has advertised a non-zero SETTINGS_QPACK_MAX_TABLE_CAPACITY.
        // We start static-only (0) and will update after receiving peer SETTINGS.
        _requestEncoder = new RequestEncoder(maxTableCapacity: 0);
        Tracker = new StreamTracker(0, 100);

        var idleTimeout = config.IdleTimeout == TimeSpan.Zero
            ? DefaultIdleTimeout
            : config.IdleTimeout;

        Connection = new ConnectionState(idleTimeout, config.AllowServerPush ? 100 : 0);
    }

    /// <summary>
    /// Builds the HTTP/3 control stream preface if not yet sent.
    /// Emits: stream type VarInt(0x00) + SETTINGS frame + optional MAX_PUSH_ID.
    /// Returns null if already sent.
    /// </summary>
    public IOutputItem? TryBuildControlPreface()
    {
        if (_controlPrefaceSent)
        {
            return null;
        }

        _controlPrefaceSent = true;

        var settings = new Settings();
        var settingsFrame = settings.ToFrame();

        var streamTypeSize = QuicVarInt.EncodedLength((long)StreamType.Control);
        var frameSize = settingsFrame.SerializedSize;
        var totalSize = streamTypeSize + frameSize;

        Http3MaxPushIdFrame? maxPushIdFrame = null;
        if (_config.AllowServerPush)
        {
            maxPushIdFrame = new Http3MaxPushIdFrame(99);
            totalSize += maxPushIdFrame.SerializedSize;
        }

        using var owner = MemoryPool<byte>.Shared.Rent(totalSize);
        var span = owner.Memory.Span;

        var written = QuicVarInt.Encode((long)StreamType.Control, span);
        span = span[written..];
        settingsFrame.WriteTo(ref span);

        maxPushIdFrame?.WriteTo(ref span);

        var buf = NetworkBuffer.Rent(totalSize);
        owner.Memory.Span[..totalSize].CopyTo(buf.FullMemory.Span);
        buf.Length = totalSize;
        buf.Key = Endpoint;

        return new Http3OutputTaggedItem(buf, OutputStreamType.Control);
    }

    /// <summary>
    /// Decodes a NetworkBuffer into HTTP/3 frames.
    /// </summary>
    public IReadOnlyList<Http3Frame> DecodeServerData(NetworkBuffer buffer)
    {
        var frames = _frameDecoder.DecodeAll(buffer.Memory.Span, out _);
        buffer.Dispose();
        return frames;
    }

    /// <summary>
    /// Processes a single decoded HTTP/3 frame. Calls <see cref="IStageOperations"/>
    /// for responses, signals, and warnings. Sets <see cref="ResponseProduced"/> if a response was generated.
    /// Returns the frame if it should be forwarded for response assembly, or null if absorbed.
    /// </summary>
    public Http3Frame? ProcessFrame(Http3Frame frame)
    {
        Connection.RecordActivity();

        switch (frame)
        {
            case Http3SettingsFrame settings:
                HandleSettings(settings);
                return null;

            case Http3GoAwayFrame goAway:
                HandleGoAway(goAway);
                return null;

            case Http3PushPromiseFrame pushPromise:
                return HandlePushPromise(pushPromise);

            case Http3CancelPushFrame cancelPush:
                Connection.OnReceivedCancelPush(cancelPush);
                return null;

            case Http3MaxPushIdFrame:
                return null;

            case Http3HeadersFrame:
                if (Connection.ActiveStreamCount > 0)
                {
                    Connection.OnStreamClosed();
                }

                return frame;

            default:
                return frame;
        }
    }

    /// <summary>
    /// Assembles a response from an HTTP/3 frame (HEADERS or DATA) on the given stream.
    /// Routes to per-stream state so multiple responses can be assembled concurrently.
    /// </summary>
    public void AssembleResponse(Http3Frame frame, long streamId)
    {
        ResponseProduced = false;

        if (!_streams.TryGetValue(streamId, out var state))
        {
            state = RentStreamState(streamId);
            _streams[streamId] = state;
        }

        switch (frame)
        {
            case Http3HeadersFrame headers:
                HandleResponseHeaders(headers, state);
                break;

            case Http3DataFrame data:
                HandleResponseData(data, state);
                break;
        }
    }

    /// <summary>
    /// Completes response assembly for a specific stream (QUIC FIN on request stream).
    /// </summary>
    public void FlushPendingResponse(long streamId)
    {
        if (_streams.TryGetValue(streamId, out var state) && state.HasResponse)
        {
            EmitResponse(streamId);
        }
    }

    /// <summary>
    /// Completes all in-progress response assemblies (upstream finish / connection close).
    /// </summary>
    public void FlushPendingResponse()
    {
        // Snapshot keys — EmitResponse modifies _streams via ReturnStreamState
        var streamIds = _streams.Keys.ToArray();
        foreach (var streamId in streamIds)
        {
            if (_streams.TryGetValue(streamId, out var state) && state.HasResponse)
            {
                EmitResponse(streamId);
            }
        }
    }

    /// <summary>
    /// Encodes an outbound HTTP request into HTTP/3 frames and emits them via callbacks.
    /// Also handles QPACK encoder instructions and correlation tracking.
    /// Returns false if GOAWAY was received (request dropped).
    /// </summary>
    public bool EncodeRequest(HttpRequestMessage request)
    {
        if (Connection.GoAwayReceived)
        {
            _ops.OnWarning("RFC 9114 §5.2 — GOAWAY received; dropping outbound request.");
            return false;
        }

        if (IsReconnecting)
        {
            // Buffer the raw frames for replay after reconnection
            var frames = EncodeToFrames(request);
            foreach (var f in frames)
            {
                _reconnectBuffer.Add(f);
            }

            // Allocate stream ID for correlation even during reconnect
            var reconnectStreamId = Tracker.AllocateStreamId();
            _correlationMap[reconnectStreamId] = request;
            return true;
        }

        var encoded = EncodeToFrames(request);

        // Extract and store endpoint for Key propagation (mirrors HTTP/2 pattern)
        var endpoint = request.RequestUri is not null
            ? RequestEndpoint.FromRequest(request)
            : RequestEndpoint.Default;

        if (Endpoint == default && endpoint != default)
        {
            Endpoint = endpoint;
        }

        // Track stream lifecycle
        var streamId = Tracker.AllocateStreamId();
        Tracker.OnStreamOpened(streamId);
        Connection.OnStreamOpened();

        // Correlate by stream ID
        _correlationMap[streamId] = request;

        // Emit QPACK encoder instructions (if any) before request frames
        FlushEncoderInstructions();

        // Serialize and emit request frames tagged with the stream ID
        // so the transport can route them to the correct QUIC stream.
        foreach (var f in encoded)
        {
            EmitSerializedFrame(f, streamId);
        }

        // RFC 9114 §4.1: signal end-of-request so the transport can close the write side (FIN).
        _ops.OnOutbound(new Http3EndOfRequestItem { Key = endpoint, StreamId = streamId });

        return true;
    }

    /// <summary>
    /// Encodes an HttpRequestMessage into HTTP/3 frames (HEADERS + DATA).
    /// Tags HEADERS with EarlyData flag for idempotent methods when configured.
    /// </summary>
    private IReadOnlyList<Http3Frame> EncodeToFrames(HttpRequestMessage request)
    {
        OriginValidator.Validate(request.RequestUri!, request.Method == HttpMethod.Connect);
        var frames = _requestEncoder.Encode(request);

        if (_config.AllowEarlyData && IdempotentMethods.Contains(request.Method))
        {
            foreach (var f in frames)
            {
                if (f is Http3HeadersFrame headers)
                {
                    headers.EarlyData = true;
                }
            }
        }

        return frames;
    }

    /// <summary>
    /// Processes bytes from the inbound QPACK decoder stream.
    /// Deserializes decoder instructions and applies them to the encoder state.
    /// </summary>
    public void ProcessQpackDecoderBytes(ReadOnlyMemory<byte> data)
    {
        try
        {
            var instructions = _instructionDecoder.DecodeAllDecoderInstructions(data.Span);

            foreach (var instruction in instructions)
            {
                _requestEncoder.QpackEncoder.ApplyDecoderInstruction(instruction);
            }
        }
        catch (Exception ex)
        {
            _ops.OnWarning($"QPACK decoder stream error absorbed — {ex.Message}");
        }
    }

    /// <summary>
    /// Serializes any pending QPACK encoder instructions and emits them
    /// as tagged items on the encoder stream. Prepends the stream type prefix
    /// (VarInt 0x02) on first emission.
    /// </summary>
    public void FlushEncoderInstructions()
    {
        var instructions = _requestEncoder.EncoderInstructions;
        if (instructions.Length == 0)
        {
            return;
        }

        int totalLength;
        using var owner = MemoryPool<byte>.Shared.Rent(1 + instructions.Length);
        var span = owner.Memory.Span;

        if (!_qpackEncoderPrefaceSent)
        {
            _qpackEncoderPrefaceSent = true;
            span[0] = 0x02; // QPACK encoder stream type
            instructions.Span.CopyTo(span[1..]);
            totalLength = 1 + instructions.Length;
        }
        else
        {
            instructions.Span.CopyTo(span);
            totalLength = instructions.Length;
        }

        var buf = NetworkBuffer.Rent(totalLength);
        owner.Memory.Span[..totalLength].CopyTo(buf.FullMemory.Span);
        buf.Length = totalLength;
        buf.Key = Endpoint;

        _ops.OnOutbound(new Http3OutputTaggedItem(buf, OutputStreamType.QpackEncoder));
    }

    private void EmitSerializedFrame(Http3Frame frame, long streamId = -1)
    {
        var buf = NetworkBuffer.Rent(frame.SerializedSize);
        var span = buf.FullMemory.Span;
        frame.WriteTo(ref span);
        buf.Length = frame.SerializedSize;
        buf.Key = Endpoint;

        if (streamId >= 0)
        {
            _ops.OnOutbound(new Http3OutputTaggedItem(buf, OutputStreamType.Request, streamId));
        }
        else
        {
            _ops.OnOutbound(buf);
        }
    }

    private void HandleResponseHeaders(Http3HeadersFrame frame, StreamState state)
    {
        if (state.HasResponse)
        {
            // Trailing HEADERS — skip (trailers not yet supported)
            return;
        }

        var headers = _qpackDecoder.Decode(frame.HeaderBlock.Span);
        FieldValidator.ValidateResponsePseudoHeaders(headers);
        FieldValidator.Validate(headers);

        var response = state.InitResponse();

        foreach (var h in headers)
        {
            if (h.Name == ":status")
            {
                response.StatusCode = (HttpStatusCode)int.Parse(h.Value);
            }
            else if (!h.Name.StartsWith(':'))
            {
                response.Headers.TryAddWithoutValidation(h.Name, h.Value);

                if (IsContentHeader(h.Name))
                {
                    state.AddContentHeader(h.Name, h.Value);
                }
            }
        }
    }

    private void HandleResponseData(Http3DataFrame frame, StreamState state)
    {
        if (!state.HasResponse)
        {
            _ops.OnWarning("RFC 9114 §4.1 — DATA frame received before HEADERS; dropping.");
            return;
        }

        var data = frame.Data.Span;
        if (data.Length == 0)
        {
            return;
        }

        state.AppendBody(data);
    }

    private void EmitResponse(long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var state) || !state.HasResponse)
        {
            return;
        }

        var response = state.GetResponse();
        var (bodyOwner, bodyLength) = state.TakeBodyOwnership();

        if (bodyLength > 0 && bodyOwner is not null)
        {
            response.Content = new PooledBodyContent(bodyOwner, bodyLength);
            state.ApplyContentHeadersTo(response.Content);
        }
        else
        {
            bodyOwner?.Dispose();
        }

        // Correlate with the original request by stream ID
        if (_correlationMap.TryGetValue(streamId, out var request))
        {
            response.RequestMessage = request;
            _correlationMap.Remove(streamId);
        }

        ResponseProduced = true;
        _ops.OnResponse(response);

        ReturnStreamState(streamId);
    }

    private static bool IsContentHeader(string name) =>
        name.StartsWith("content-", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("allow", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("expires", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("last-modified", StringComparison.OrdinalIgnoreCase);


    private void HandleSettings(Http3SettingsFrame settings)
    {
        try
        {
            Connection.OnRemoteSettings(settings);
            _ops.OnWarning(
                $"RFC 9114 §7.2.4 — remote SETTINGS received ({settings.Parameters.Count} parameters).");
        }
        catch (Http3Exception ex)
        {
            _ops.OnWarning($"SETTINGS error absorbed — {ex.Message}");
        }
    }

    private void HandleGoAway(Http3GoAwayFrame goAway)
    {
        try
        {
            Connection.OnServerGoAway(goAway);
            _ops.OnWarning(
                $"RFC 9114 §5.2 — GOAWAY received (streamId={goAway.StreamId}).");
        }
        catch (Http3Exception ex)
        {
            _ops.OnWarning($"GOAWAY error absorbed — {ex.Message}");
            Connection.GoAwayReceived = true;
        }
    }

    private Http3Frame? HandlePushPromise(Http3PushPromiseFrame pushPromise)
    {
        if (!_config.AllowServerPush)
        {
            var cancelFrame = new Http3CancelPushFrame(pushPromise.PushId);
            EmitSerializedFrame(cancelFrame);
            _ops.OnWarning(
                $"RFC 9114 §7.2.5 — push promise rejected (pushId={pushPromise.PushId}); AllowServerPush=false.");
            return null;
        }

        try
        {
            Connection.RecordPush();
        }
        catch (Http3Exception ex)
        {
            _ops.OnWarning($"Push limit exceeded — {ex.Message}");
            return null;
        }

        return pushPromise;
    }

    /// <summary>
    /// Checks whether the idle timeout has expired with no active streams.
    /// Returns the GOAWAY frame to send if expired, or null if still active.
    /// </summary>
    public Http3GoAwayFrame? CheckIdleTimeout()
    {
        if (Connection.IsIdleTimeoutExpired() && Connection.ActiveStreamCount == 0)
        {
            _ops.OnWarning("RFC 9114 §5.1 — idle timeout expired with no active streams; sending GOAWAY.");
            return new Http3GoAwayFrame(0);
        }

        return null;
    }

    /// <summary>Whether the idle timeout is disabled (timeout is zero).</summary>
    public bool IsTimeoutDisabled => Connection.IsTimeoutDisabled;

    /// <summary>Time remaining before the idle timeout expires.</summary>
    public TimeSpan TimeUntilExpiry() => Connection.TimeUntilExpiry();

    /// <summary>
    /// Called when the QUIC connection is lost. Enters reconnect state and
    /// buffers any pending outbound frames for replay.
    /// </summary>
    public void OnConnectionLost()
    {
        IsReconnecting = true;
        _reconnectAttempts = 1;

        // Dispose and pool all per-stream state; keep correlation map for replay
        foreach (var (_, state) in _streams)
        {
            state.Reset();
            if (_statePool.Count < MaxPoolSize)
            {
                _statePool.Push(state);
            }
        }

        _streams.Clear();

        Tracker.Reset();
        Connection.Reset();
        _frameDecoder.Reset();
        _instructionDecoder.Reset();
        _controlPrefaceSent = false;
        _qpackEncoderPrefaceSent = false;
    }

    /// <summary>
    /// Called when a new QUIC connection is established after a loss.
    /// Replays buffered frames via the outbound callback.
    /// </summary>
    public void OnConnectionRestored()
    {
        IsReconnecting = false;
        _reconnectAttempts = 0;

        // Re-emit preface on new connection
        var preface = TryBuildControlPreface();
        if (preface is not null)
        {
            _ops.OnOutbound(preface);
        }

        // Snapshot and clear old correlation — we'll re-correlate with new stream IDs
        var oldCorrelations = _correlationMap.Values.ToList();
        _correlationMap.Clear();

        var toReplay = _reconnectBuffer.ToList();
        _reconnectBuffer.Clear();

        var correlationIndex = 0;
        long currentReplayStreamId = -1;
        foreach (var frame in toReplay)
        {
            // Track stream lifecycle for replayed HEADERS and re-correlate
            if (frame is Http3HeadersFrame)
            {
                currentReplayStreamId = Tracker.AllocateStreamId();
                Tracker.OnStreamOpened(currentReplayStreamId);
                Connection.OnStreamOpened();

                if (correlationIndex < oldCorrelations.Count)
                {
                    _correlationMap[currentReplayStreamId] = oldCorrelations[correlationIndex++];
                }
            }

            EmitSerializedFrame(frame, currentReplayStreamId);
        }
    }

    /// <summary>
    /// Called when a reconnect attempt fails. Increments the attempt counter.
    /// If max attempts exhausted, signals <see cref="IStageOperations.OnReconnectFailed"/>.
    /// Returns true if another attempt should be made, false if max exceeded.
    /// </summary>
    public bool OnReconnectAttemptFailed()
    {
        if (_reconnectAttempts >= _config.MaxReconnectAttempts)
        {
            _ops.OnReconnectFailed();
            return false;
        }

        _reconnectAttempts++;
        return true;
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

        state.Reset();
        if (_statePool.Count < MaxPoolSize)
        {
            _statePool.Push(state);
        }
    }

    /// <summary>
    /// Disposes owned resources (frame decoder, per-stream state, instruction decoder).
    /// </summary>
    public void Dispose()
    {
        _frameDecoder.Dispose();
        _instructionDecoder.Dispose();

        foreach (var state in _streams.Values)
        {
            state.Reset();
        }

        _streams.Clear();

        while (_statePool.TryPop(out _))
        {
            // Pool entries are already reset — just drain
        }
    }
}