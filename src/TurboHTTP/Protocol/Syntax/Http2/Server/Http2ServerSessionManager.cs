using System.Buffers;
using System.Text;
using Akka.Actor;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Multiplexed;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Options;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;
using static Servus.Senf;

namespace TurboHTTP.Protocol.Syntax.Http2.Server;

internal sealed class Http2ServerSessionManager
{
    private const int MaxStatePoolCapacity = 1000;

    // RFC 9113 §5.1 / CVE-2023-44487 (Rapid Reset): client-initiated resets are counted within this
    // sliding window; exceeding the configured budget closes the connection with ENHANCE_YOUR_CALM.
    private const long ResetWindowMs = 30_000;

    private const string DataRateCheck = "data-rate-check";

    private readonly StackStreamStatePool<StreamState> _statePool;

    private readonly Http2ServerEncoderOptions _encoderOptions;
    private readonly Http2ServerDecoderOptions _decoderOptions;
    private readonly IServerStageOperations _ops;
    private readonly FrameDecoder _frameDecoder;
    private readonly Http2ServerDecoder _requestDecoder;
    private readonly Http2ServerEncoder _responseEncoder;
    private readonly FlowController _flow;
    private readonly StreamTracker _tracker;
    private readonly long _maxRequestBodySize;
    private readonly BodyEncoderOptions _bodyEncoderOptions;
    private readonly TimeSpan _bodyConsumptionTimeout;
    private readonly int _initialStreamWindowSize;

    private readonly Dictionary<int, StreamState> _streams = new();

    private readonly record struct StreamBodyReadComplete(int StreamId, int BytesRead);

    private readonly record struct StreamBodyReadFailed(int StreamId, Exception Reason);

    private readonly Dictionary<int, Stream> _activeBodyStreams = new();
    private readonly Dictionary<int, IMemoryOwner<byte>> _activeBodyBuffers = new();
    private readonly Dictionary<int, CancellationTokenSource> _activeBodyReadCts = new();
    // Streams whose reusable response-drain buffer currently has an async ReadAsync in flight.
    // The buffer must not be returned to the pool until that read completes, or a concurrent
    // stream could re-rent and overwrite it mid-read (cross-stream, wrong-content corruption).
    private readonly HashSet<int> _drainReadInFlight = new();
    // Streams torn down while a drain read was in flight: disposal is deferred to the
    // read-completion handler.
    private readonly HashSet<int> _drainBufferOrphaned = new();

    private int _nextContinuationStreamId;
    private bool _continuationEndStream;
    private readonly DataRateMonitor _requestRate;
    private readonly DataRateMonitor _responseRate;
    private readonly List<long> _rateViolations = [];
    private readonly HashSet<long> _rateViolationSet = [];
    private bool _rateTimerActive;
    private readonly TimeProvider _clock;
    private bool _prefaceConsumed;


    private readonly int _maxResetStreamsPerWindow;
    private int _resetCount;
    private long _resetWindowStart;

    private bool _awaitingPingAck;
    private long _pingSentTimestamp;

    private long Now() => _clock.GetUtcNow().ToUnixTimeMilliseconds();

    public int ActiveStreamCount => _streams.Count;
    public int MaxConcurrentStreams => _decoderOptions.MaxConcurrentStreams;

    public Http2ServerSessionManager(
        Http2ConnectionOptions options,
        IServerStageOperations ops,
        TimeProvider? timeProvider = null)
    {
        _clock = timeProvider ?? TimeProvider.System;
        _encoderOptions = options.ToEncoderOptions();
        _decoderOptions = options.ToDecoderOptions();
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));

        _responseEncoder = new Http2ServerEncoder(_encoderOptions);
        _requestDecoder = new Http2ServerDecoder(_decoderOptions);
        // RFC 9113 §4.2: enforce the MAX_FRAME_SIZE we advertise in SETTINGS on inbound frames.
        _frameDecoder = new FrameDecoder(_encoderOptions.MaxFrameSize);
        WindowScaler? scaler = null;
        if (_decoderOptions.EnableAdaptiveWindowScaling)
        {
            scaler = new WindowScaler(
                _decoderOptions.MaxStreamWindowSize,
                _decoderOptions.WindowScaleThresholdMultiplier);
        }

        _flow = new FlowController(
            options.InitialConnectionWindowSize,
            options.InitialStreamWindowSize,
            scaler,
            _clock);
        _tracker = new StreamTracker(initialNextStreamId: 1, options.MaxConcurrentStreams);
        _maxRequestBodySize = options.Limits.MaxRequestBodySize;
        _maxResetStreamsPerWindow = options.Limits.MaxResetStreamsPerWindow;
        _bodyEncoderOptions = options.ToBodyEncoderOptions();
        _bodyConsumptionTimeout = options.BodyConsumptionTimeout;
        _initialStreamWindowSize = options.InitialStreamWindowSize;

        var rate = options.ToRateMonitor();
        _requestRate = new DataRateMonitor(rate.MinRequestBodyDataRate, rate.MinRequestBodyDataRateGracePeriod);
        _responseRate = new DataRateMonitor(rate.MinResponseDataRate, rate.MinResponseDataRateGracePeriod);

        var statePoolCapacity = Math.Min(
            options.MaxConcurrentStreams > 0 ? options.MaxConcurrentStreams : 100,
            MaxStatePoolCapacity);
        _statePool = new StackStreamStatePool<StreamState>(
            statePoolCapacity,
            () => new StreamState());
    }

    public void PreStart()
    {
        var settingsParams = new[]
        {
            (SettingsParameter.MaxConcurrentStreams, (uint)_decoderOptions.MaxConcurrentStreams),
            (SettingsParameter.InitialWindowSize, (uint)_initialStreamWindowSize),
            (SettingsParameter.MaxFrameSize, (uint)_encoderOptions.MaxFrameSize),
            (SettingsParameter.HeaderTableSize, (uint)_encoderOptions.HeaderTableSize)
        };

        var settingsFrame = new SettingsFrame(settingsParams, isAck: false);
        EmitFrame(settingsFrame);

        var connectionWindowIncrement = _flow.RecvConnectionWindow - 65535;
        if (connectionWindowIncrement > 0)
        {
            EmitFrame(new WindowUpdateFrame(0, connectionWindowIncrement));
        }
    }

    /// <summary>
    /// True once a connection-fatal protocol error (or graceful teardown) has occurred. The owning
    /// state machine surfaces this so the stage flushes the pending GOAWAY and closes the connection.
    /// </summary>
    public bool ShouldComplete { get; internal set; }

    public void DecodeClientData(TransportBuffer buffer)
    {
        try
        {
            if (!_prefaceConsumed)
            {
                SkipConnectionPreface(buffer);
            }

            var frames = _frameDecoder.Decode(buffer);
            for (var i = 0; i < frames.Count; i++)
            {
                ProcessFrame(frames[i]);
            }
        }
        catch (StreamProtocolException e)
        {
            // RFC 9113 §5.4.2: stream-scoped error - reset just that stream, keep the connection.
            EmitRstStream(e.StreamId, (Http2ErrorCode)e.ErrorCode);
        }
        catch (ConnectionProtocolException e)
        {
            TerminateConnection((Http2ErrorCode)e.ErrorCode, e.Message);
        }
        catch (HpackException e)
        {
            // RFC 9113 §4.3: HPACK decoding failures are a connection-level COMPRESSION_ERROR; the
            // dynamic table is now desynchronized so the connection cannot continue.
            TerminateConnection(Http2ErrorCode.CompressionError, e.Message);
        }
        catch (HuffmanException e)
        {
            TerminateConnection(Http2ErrorCode.CompressionError, e.Message);
        }
        catch (HttpProtocolException e)
        {
            // RFC 9113 §5.4.1: any other framing/protocol violation is connection-fatal.
            TerminateConnection(Http2ErrorCode.ProtocolError, e.Message);
        }
    }

    private void TerminateConnection(Http2ErrorCode errorCode, string reason)
    {
        Tracing.For("Protocol").Warning(this,
            "HTTP/2: connection terminated ({0}): {1}", errorCode, reason);
        EmitGoAway(_tracker.HighestAcceptedStreamId, errorCode, reason);
        ShouldComplete = true;
    }

    private static ReadOnlySpan<byte> ConnectionPrefaceMagic => "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

    private void SkipConnectionPreface(TransportBuffer buffer)
    {
        _prefaceConsumed = true;

        var span = buffer.Memory.Span;
        if (span.Length >= ConnectionPrefaceMagic.Length
            && span[..ConnectionPrefaceMagic.Length].SequenceEqual(ConnectionPrefaceMagic))
        {
            var remaining = span.Length - ConnectionPrefaceMagic.Length;
            span[ConnectionPrefaceMagic.Length..].CopyTo(span);
            buffer.Length = remaining;
        }
    }

    private void ProcessFrame(Http2Frame frame)
    {
        switch (frame)
        {
            case HeadersFrame headers:
                HandleHeadersFrame(headers);
                break;

            case ContinuationFrame continuation:
                HandleContinuationFrame(continuation);
                break;

            case DataFrame data:
                HandleDataFrame(data);
                break;

            case SettingsFrame settings:
                HandleSettingsFrame(settings);
                break;

            case WindowUpdateFrame windowUpdate:
                HandleWindowUpdateFrame(windowUpdate);
                break;

            case PingFrame ping:
                HandlePingFrame(ping);
                break;

            case GoAwayFrame:
                HandleGoAwayFrame();
                break;

            case RstStreamFrame rst:
                HandleRstStreamFrame(rst);
                break;
        }
    }

    public void OnResponse(IFeatureCollection features)
    {
        var streamId = GetStreamIdFromFeatures(features);
        if (!_streams.TryGetValue(streamId, out var state))
        {
            Tracing.For("Protocol").Warning(this, "HTTP/2: Response for unknown stream {0}", streamId);
            return;
        }

        state.SetFeatures(features);

        if (state.HasBodyReader && _bodyConsumptionTimeout > TimeSpan.Zero)
        {
            _ops.OnScheduleTimer(state.BodyConsumptionTimerKey, _bodyConsumptionTimeout);
        }

        var responseFeature = features.Get<IHttpResponseFeature>();
        var responseBody = features.Get<IHttpResponseBodyFeature>();
        var contentLength = ExtractContentLength(responseFeature);
        var hasBody = contentLength is not null and not 0
                      || (contentLength is null && responseBody is TurboHttpResponseBodyFeature { HasStarted: true });

        var frames = _responseEncoder.EncodeHeaders(features, streamId, hasBody);
        for (var i = 0; i < frames.Count; i++)
        {
            EmitFrame(frames[i]);
        }

        if (!hasBody || responseBody is not TurboHttpResponseBodyFeature turboBody)
        {
            CloseStream(streamId);
            return;
        }

        if (turboBody.TryGetBufferedBody(out var bufferedBody))
        {
            if (bufferedBody.Length > 0)
            {
                var window = _flow.GetSendWindow(streamId);
                if (window >= bufferedBody.Length)
                {
                    var maxFrame = _responseEncoder.MaxFrameSize;
                    var remaining = bufferedBody;
                    while (remaining.Length > maxFrame)
                    {
                        EmitFrame(new DataFrame(streamId, remaining[..maxFrame], endStream: false));
                        remaining = remaining[maxFrame..];
                    }

                    EmitFrame(new DataFrame(streamId, remaining, endStream: true));
                    _flow.OnDataSent(streamId, bufferedBody.Length);
                    CloseStream(streamId);
                    return;
                }

                SendBufferedBodyWithFlowControl(streamId, state, bufferedBody, window);
                return;
            }
            else
            {
                EmitFrame(new DataFrame(streamId, ReadOnlyMemory<byte>.Empty, endStream: true));
                CloseStream(streamId);
                return;
            }
        }

        var bodyStream = turboBody.GetResponseStream();
        state.MarkBodyDrainActive();
        StartStreamBodyDrain(streamId, bodyStream, contentLength);
        Tracing.For("Protocol").Debug(this, "HTTP/2: response body drain started (stream={0})", streamId);
    }

    private static long? ExtractContentLength(IHttpResponseFeature? responseFeature)
    {
        if (responseFeature?.Headers is null)
        {
            return null;
        }

        foreach (var header in responseFeature.Headers)
        {
            if (header.Key.Equals(WellKnownHeaders.ContentLength, StringComparison.OrdinalIgnoreCase) &&
                header.Value.FirstOrDefault() is { } value && ContentLengthSemantics.TryParse(value, out var length))
            {
                return length;
            }
        }

        return null;
    }

    public void OnBodyMessage(object msg)
    {
        switch (msg)
        {
            case StreamBodyReadComplete read:
                HandleStreamBodyRead(read);
                break;

            case StreamBodyReadFailed failed:
                _drainReadInFlight.Remove(failed.StreamId);
                if (_drainBufferOrphaned.Remove(failed.StreamId))
                {
                    // Stream already torn down while this read was in flight (the read failed
                    // because CleanupBodyDrain cancelled it). Release the deferred buffer; the
                    // stream is gone, so no RST is needed.
                    DisposeDrainResources(failed.StreamId);
                    break;
                }

                Tracing.For("Protocol").Warning(this,
                    "HTTP/2: Response body drain failed for stream {0}: {1}", failed.StreamId,
                    failed.Reason.Message);
                EmitRstStream(failed.StreamId, Http2ErrorCode.InternalError);
                CleanupBodyDrain(failed.StreamId);
                break;
        }
    }

    private void HandleStreamBodyRead(StreamBodyReadComplete read)
    {
        _drainReadInFlight.Remove(read.StreamId);
        if (_drainBufferOrphaned.Remove(read.StreamId))
        {
            // The stream was torn down while this read was in flight; the buffer was kept
            // alive for the read. Release it now and drop the result — the stream is gone.
            DisposeDrainResources(read.StreamId);
            return;
        }

        if (!_streams.TryGetValue(read.StreamId, out var state))
        {
            CleanupBodyDrain(read.StreamId);
            return;
        }

        state.IsBodyReadPending = false;

        if (read.BytesRead == 0)
        {
            Tracing.For("Protocol").Debug(this, "HTTP/2: response body complete (stream={0})", read.StreamId);
            state.MarkBodyDrainComplete();

            if (!state.HasPendingOutbound)
            {
                EmitEndOfBody(read.StreamId, state);
                CleanupBodyDrain(read.StreamId);
                CloseStream(read.StreamId);
            }
            else
            {
                CleanupBodyDrain(read.StreamId);
            }

            return;
        }

        Tracing.For("Protocol").Trace(this, "HTTP/2: response body chunk (stream={0}, bytes={1})", read.StreamId, read.BytesRead);
        if (!_activeBodyBuffers.TryGetValue(read.StreamId, out var buffer))
        {
            CleanupBodyDrain(read.StreamId);
            return;
        }

        var data = buffer.Memory[..read.BytesRead];
        var window = _flow.GetSendWindow(read.StreamId);

        if (window >= read.BytesRead)
        {
            EmitFrame(new DataFrame(read.StreamId, data, endStream: false));
            _flow.OnDataSent(read.StreamId, read.BytesRead);
            ReadNextBodyChunk(read.StreamId);
        }
        else if (window > 0)
        {
            EmitFrame(new DataFrame(read.StreamId, data[..(int)window], endStream: false));
            _flow.OnDataSent(read.StreamId, (int)window);
            var remaining = read.BytesRead - (int)window;
            var owner = MemoryPool<byte>.Shared.Rent(remaining);
            data[(int)window..].CopyTo(owner.Memory);
            state.EnqueueBodyChunk(new StreamBodyChunk(owner, remaining));
        }
        else
        {
            var owner = MemoryPool<byte>.Shared.Rent(read.BytesRead);
            data[..read.BytesRead].CopyTo(owner.Memory);
            state.EnqueueBodyChunk(new StreamBodyChunk(owner, read.BytesRead));
        }
    }

    private void EmitEndOfBody(int streamId, StreamState state)
    {
        var features = state.GetFeatures();
        var trailerFeature = features?.Get<IHttpResponseTrailersFeature>();
        var hasTrailers = trailerFeature?.Trailers.Count > 0;

        if (hasTrailers)
        {
            EmitFrame(new DataFrame(streamId, ReadOnlyMemory<byte>.Empty, endStream: false));
            var trailerFrames = _responseEncoder.EncodeTrailers(streamId, trailerFeature!.Trailers);
            for (var i = 0; i < trailerFrames.Count; i++)
            {
                EmitFrame(trailerFrames[i]);
            }
        }
        else
        {
            EmitFrame(new DataFrame(streamId, ReadOnlyMemory<byte>.Empty, endStream: true));
        }
    }

    public void DrainOutboundBuffer(int streamId)
    {
        if (!_streams.TryGetValue(streamId, out var state))
        {
            return;
        }

        if (!state.HasPendingOutbound)
        {
            if (state.HasBodyDrain && !state.IsBodyDrainComplete && !state.IsBodyReadPending)
            {
                ReadNextBodyChunk(streamId);
            }

            return;
        }

        while (state.PeekBodyChunk() is { } next)
        {
            var window = _flow.GetSendWindow(streamId);
            if (window <= 0)
            {
                break;
            }

            state.TryDequeueBodyChunk(out var chunk);
            if (window >= chunk!.Length)
            {
                EmitFrame(new DataFrame(streamId, chunk.Owner.Memory[..chunk.Length], endStream: false));
                _flow.OnDataSent(streamId, chunk.Length);
                chunk.Owner.Dispose();
            }
            else
            {
                var sendable = (int)window;
                EmitFrame(new DataFrame(streamId, chunk.Owner.Memory[..sendable], endStream: false));
                _flow.OnDataSent(streamId, sendable);
                var remaining = chunk.Length - sendable;
                var owner = MemoryPool<byte>.Shared.Rent(remaining);
                chunk.Owner.Memory.Slice(sendable, remaining).CopyTo(owner.Memory);
                chunk.Owner.Dispose();
                state.PrependBodyChunk(new StreamBodyChunk(owner, remaining));
                break;
            }
        }

        if (state is { HasPendingOutbound: false, IsBodyDrainComplete: true })
        {
            EmitEndOfBody(streamId, state);
            CloseStream(streamId);
        }
        else if (!state.HasPendingOutbound && state.HasBodyDrain && !state.IsBodyDrainComplete && !state.IsBodyReadPending)
        {
            ReadNextBodyChunk(streamId);
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

    public bool IsKeepAliveTimedOut(TimeSpan timeout)
    {
        if (!_awaitingPingAck)
        {
            return false;
        }

        var elapsed = Environment.TickCount64 - _pingSentTimestamp;
        return elapsed >= (long)timeout.TotalMilliseconds;
    }

    public void Cleanup()
    {
        foreach (var (_, state) in _streams)
        {
            state.AbortBody();
        }

        // Release response-drain resources. A buffer with a still-in-flight read is abandoned
        // to the GC rather than returned to the pool — returning a buffer a read is still
        // writing into is exactly the cross-stream corruption we guard against elsewhere.
        foreach (var (streamId, buffer) in _activeBodyBuffers)
        {
            if (!_drainReadInFlight.Contains(streamId))
            {
                buffer.Dispose();
            }
        }

        foreach (var (_, cts) in _activeBodyReadCts)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _activeBodyBuffers.Clear();
        _activeBodyStreams.Clear();
        _activeBodyReadCts.Clear();
        _drainReadInFlight.Clear();
        _drainBufferOrphaned.Clear();

        _frameDecoder.Dispose();

        foreach (var state in _streams.Values)
        {
            state.Reset();
            _statePool.Return(state);
        }

        _streams.Clear();
    }

    private void HandleHeadersFrame(HeadersFrame headers)
    {
        var streamId = headers.StreamId;

        if (_nextContinuationStreamId != 0)
        {
            EmitRstStream(streamId, Http2ErrorCode.ProtocolError);
            return;
        }

        var isTrailer = _streams.TryGetValue(streamId, out var existing) && existing.GetRequestFeature() is not null;

        if (!isTrailer)
        {
            var acceptResult = _tracker.TryAcceptClientStream(streamId);
            switch (acceptResult)
            {
                case StreamAcceptResult.InvalidId:
                    TerminateConnection(Http2ErrorCode.ProtocolError,
                        "RFC 9113 §5.1.1: client stream ID must be odd and non-zero.");
                    return;
                case StreamAcceptResult.NonMonotonic:
                    TerminateConnection(Http2ErrorCode.ProtocolError,
                        "RFC 9113 §5.1.1: stream ID must be monotonically increasing.");
                    return;
                case StreamAcceptResult.RefusedStream:
                    EmitRstStream(streamId, Http2ErrorCode.RefusedStream);
                    return;
            }
        }

        var state = isTrailer ? existing! : GetOrCreateStreamState(streamId);

        if (headers.EndHeaders)
        {
            if (isTrailer)
            {
                state.AppendHeader(headers.HeaderBlockFragment.Span, _decoderOptions.MaxHeaderBytes);
                HandleTrailers(streamId, state);
            }
            else
            {
                state.AppendHeader(headers.HeaderBlockFragment.Span, _decoderOptions.MaxHeaderBytes);
                DecodeAndEmitRequest(streamId, state, headers.EndStream);
            }
        }
        else
        {
            state.AppendHeader(headers.HeaderBlockFragment.Span, _decoderOptions.MaxHeaderBytes);
            _nextContinuationStreamId = streamId;
            _continuationEndStream = headers.EndStream;
            _ops.OnScheduleTimer(state.HeadersTimeoutTimerKey, TimeSpan.FromSeconds(30));
        }
    }

    private void HandleContinuationFrame(ContinuationFrame continuation)
    {
        var streamId = continuation.StreamId;

        if (_nextContinuationStreamId != streamId)
        {
            EmitRstStream(streamId, Http2ErrorCode.ProtocolError);
            return;
        }

        if (!_streams.TryGetValue(streamId, out var state))
        {
            EmitRstStream(streamId, Http2ErrorCode.StreamClosed);
            return;
        }

        state.AppendHeader(continuation.HeaderBlockFragment.Span, _decoderOptions.MaxHeaderBytes);

        if (continuation.EndHeaders)
        {
            var endStream = _continuationEndStream;
            _nextContinuationStreamId = 0;
            _continuationEndStream = false;
            _ops.OnCancelTimer(state.HeadersTimeoutTimerKey);
            DecodeAndEmitRequest(streamId, state, endStream);
        }
    }

    private void HandleDataFrame(DataFrame data)
    {
        var streamId = data.StreamId;

        Tracing.For("Protocol").Trace(this, "HTTP/2: DATA in (stream={0}, len={1}, endStream={2})",
            streamId, data.Data.Length, data.EndStream);

        if (!_streams.TryGetValue(streamId, out var state))
        {
            EmitRstStream(streamId, Http2ErrorCode.StreamClosed);
            return;
        }

        var flowResult = _flow.OnInboundData(streamId, data.Data.Length);

        if (flowResult.IsConnectionViolation || flowResult.IsStreamViolation)
        {
            const Http2ErrorCode errorCode = Http2ErrorCode.FlowControlError;

            if (flowResult.IsConnectionViolation)
            {
                Tracing.For("Protocol").Warning(this, "HTTP/2: connection-level flow control violation");
                EmitGoAway(0, errorCode, "Flow control violation");
            }
            else
            {
                Tracing.For("Protocol").Warning(this, "HTTP/2: stream-level flow control violation (stream={0})", streamId);
                EmitRstStream(streamId, errorCode);
            }

            return;
        }

        if (state.HasBodyReader)
        {
            try
            {
                state.FeedBody(data.Data.Span, data.EndStream);
            }
            catch (HttpProtocolException)
            {
                state.AbortBody();
                EmitRstStream(streamId, Http2ErrorCode.Cancel);
                return;
            }

            if (data.EndStream)
            {
                _ops.OnCancelTimer(state.BodyConsumptionTimerKey);
            }

            if (!data.Data.IsEmpty)
            {
                _requestRate.Observe(streamId, data.Data.Length, Now());
                EnsureRateTimer();
            }
        }

        if (flowResult.StreamWindowUpdate is { } streamWin)
        {
            EmitFrame(new WindowUpdateFrame(streamWin.StreamId, streamWin.Increment));
        }

        if (flowResult.ConnectionWindowUpdate is { } connWin)
        {
            EmitFrame(new WindowUpdateFrame(connWin.StreamId, connWin.Increment));
        }

        TrySendMeasurementPing();
    }

    private void HandleSettingsFrame(SettingsFrame settings)
    {
        if (settings.IsAck)
        {
            return;
        }

        var result = _flow.OnRemoteSettings(settings);

        if (result.AckFrame is { } ackFrame)
        {
            EmitFrame(ackFrame);
        }

        if (result.MaxConcurrentStreamsChange.HasValue)
        {
            _tracker.SetMaxConcurrentStreams(result.MaxConcurrentStreamsChange.Value);
        }

        _responseEncoder.ApplyClientSettings(settings.Parameters);

        if (result.InitialWindowSizeChange.HasValue)
        {
            foreach (var streamId in _streams.Keys.ToList())
            {
                DrainOutboundBuffer(streamId);
            }
        }
    }

    private void HandleWindowUpdateFrame(WindowUpdateFrame windowUpdate)
    {
        _flow.OnSendWindowUpdate(windowUpdate.StreamId, windowUpdate.Increment);

        if (windowUpdate.StreamId == 0)
        {
            foreach (var streamId in _streams.Keys.ToList())
            {
                DrainOutboundBuffer(streamId);
            }
        }
        else
        {
            DrainOutboundBuffer(windowUpdate.StreamId);
        }
    }

    private void HandlePingFrame(PingFrame ping)
    {
        if (ping.IsAck)
        {
            _awaitingPingAck = false;
            _flow.OnMeasurementPingAck();
            return;
        }

        var ackPing = new PingFrame(ping.Data, isAck: true);
        EmitFrame(ackPing);
    }

    private void TrySendMeasurementPing()
    {
        if (!_flow.ShouldSendMeasurementPing() || _awaitingPingAck)
        {
            return;
        }

        _awaitingPingAck = true;
        _pingSentTimestamp = Environment.TickCount64;
        var data = BitConverter.GetBytes(_pingSentTimestamp);
        _flow.OnMeasurementPingSent();
        EmitFrame(new PingFrame(data, isAck: false));
    }

    private void HandleGoAwayFrame()
    {
        Tracing.For("Protocol").Info(this, "HTTP/2: received GOAWAY from client");
        _flow.OnGoAway();
    }

    private void HandleRstStreamFrame(RstStreamFrame rst)
    {
        Tracing.For("Protocol").Debug(this, "HTTP/2: received RST_STREAM (stream={0}, error={1})", rst.StreamId, rst.ErrorCode);
        CloseStream(rst.StreamId);
        TrackStreamReset();
    }

    /// <summary>
    /// RFC 9113 §5.1 / CVE-2023-44487: counts client-initiated resets within a sliding window. A client
    /// that opens-and-resets streams faster than the configured budget is cut off with
    /// GOAWAY(ENHANCE_YOUR_CALM) - MaxConcurrentStreams alone never saturates under this attack.
    /// </summary>
    private void TrackStreamReset()
    {
        if (_maxResetStreamsPerWindow <= 0)
        {
            return;
        }

        var now = Now();
        if (now - _resetWindowStart >= ResetWindowMs)
        {
            _resetWindowStart = now;
            _resetCount = 0;
        }

        _resetCount++;
        if (_resetCount > _maxResetStreamsPerWindow)
        {
            TerminateConnection(Http2ErrorCode.EnhanceYourCalm,
                "RFC 9113 §5.1 / CVE-2023-44487: excessive stream resets.");
        }
    }

    private void HandleTrailers(int streamId, StreamState state)
    {
        try
        {
            _requestDecoder.DecodeTrailers(state);
        }
        catch (HttpProtocolException ex)
        {
            Tracing.For("Protocol")
                .Warning(this, "HTTP/2: Trailer decode error on stream {0}: {1}", streamId, ex.Message);
            EmitRstStream(streamId, Http2ErrorCode.ProtocolError);
            state.ClearHeaderBuffer();
            return;
        }

        state.ClearHeaderBuffer();
        state.FeedBody([], endStream: true);
    }

    private void DecodeAndEmitRequest(int streamId, StreamState state, bool endStream)
    {
        try
        {
            var requestFeature = _requestDecoder.DecodeHeadersToFeature(streamId, endStream: true, state);
            if (requestFeature is null)
            {
                return;
            }

            state.InitRequestFeature(requestFeature);

            _flow.InitStreamSendWindow(streamId);

            var hasBody = !endStream;
            if (hasBody)
            {
                var queued = new QueuedBodyReader(capacity: 8);
                queued.Reset();
                state.InitBodyReader(queued, _maxRequestBodySize);
                requestFeature.Body = state.GetBodyStream();
            }

            var features = FeatureCollectionFactory.Create(requestFeature, hasBody, _ops.Services,
                _ops.ConnectionFeature, _ops.TlsHandshakeFeature, _maxRequestBodySize);
            features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));

            var capturedStreamId = streamId;
            features.Set<IHttpResetFeature>(new TurboHttpResetFeature(errorCode =>
                EmitRstStream(capturedStreamId, (Http2ErrorCode)errorCode)));

            Tracing.For("Protocol").Debug(this, "HTTP/2: request dispatched (stream={0}, hasBody={1})", streamId, hasBody);
            _ops.OnRequest(features);
        }
        catch (HttpProtocolException ex)
        {
            Tracing.For("Protocol")
                .Warning(this, "HTTP/2: Header decode error on stream {0}: {1}", streamId, ex.Message);
            EmitRstStream(streamId, Http2ErrorCode.CompressionError);
        }
    }

    private static int GetStreamIdFromFeatures(IFeatureCollection features)
    {
        var streamIdFeature = features.Get<IHttpStreamIdFeature>();
        if (streamIdFeature is not null)
        {
            return (int)streamIdFeature.StreamId;
        }

        throw new InvalidOperationException(
            "Response missing stream ID. Expected IHttpStreamIdFeature in context features.");
    }

    private StreamState GetOrCreateStreamState(int streamId)
    {
        if (_streams.TryGetValue(streamId, out var existing))
        {
            return existing;
        }

        var state = _statePool.Rent();
        state.SetTimerKeys(streamId);
        _streams[streamId] = state;
        return state;
    }

    private void CloseStream(int streamId)
    {
        _requestRate.Remove(streamId);
        _responseRate.Remove(streamId);
        CleanupBodyDrain(streamId);

        if (_streams.TryGetValue(streamId, out var state))
        {
            _ops.OnCancelTimer(state.BodyConsumptionTimerKey);
            _ops.OnCancelTimer(state.HeadersTimeoutTimerKey);
            _tracker.OnStreamClosed(streamId);

            var windowUpdateSignal = _flow.OnStreamClosed(streamId);
            if (windowUpdateSignal is { } signal)
            {
                EmitFrame(new WindowUpdateFrame(signal.StreamId, signal.Increment));
            }

            _flow.RemoveStreamSendWindow(streamId);

            state.Reset();
            _statePool.Return(state);

            _streams.Remove(streamId);
        }
    }

    private void SendBufferedBodyWithFlowControl(int streamId, StreamState state, ReadOnlyMemory<byte> body, long window)
    {
        var maxFrame = _responseEncoder.MaxFrameSize;
        var sent = 0;

        if (window > 0)
        {
            var sendable = body[..(int)Math.Min(window, body.Length)];
            while (sendable.Length > maxFrame)
            {
                EmitFrame(new DataFrame(streamId, sendable[..maxFrame], endStream: false));
                sendable = sendable[maxFrame..];
            }

            EmitFrame(new DataFrame(streamId, sendable, endStream: false));
            sent = (int)Math.Min(window, body.Length);
            _flow.OnDataSent(streamId, sent);
        }

        var remainder = body[sent..];
        if (remainder.Length == 0)
        {
            EmitFrame(new DataFrame(streamId, ReadOnlyMemory<byte>.Empty, endStream: true));
            CloseStream(streamId);
            return;
        }

        state.MarkBodyDrainActive();
        state.MarkBodyDrainComplete();

        while (remainder.Length > 0)
        {
            var chunkSize = Math.Min(remainder.Length, maxFrame);
            var owner = MemoryPool<byte>.Shared.Rent(chunkSize);
            remainder[..chunkSize].CopyTo(owner.Memory);
            state.EnqueueBodyChunk(new StreamBodyChunk(owner, chunkSize));
            remainder = remainder[chunkSize..];
        }

        Tracing.For("Protocol").Debug(this,
            "HTTP/2: buffered body flow-controlled (stream={0}, sent={1}, queued={2})",
            streamId, sent, body.Length - sent);
    }

    private void StartStreamBodyDrain(int streamId, Stream bodyStream, long? contentLength = null)
    {
        _activeBodyStreams[streamId] = bodyStream;
        var maxSize = Math.Min(_bodyEncoderOptions.ChunkSize, _responseEncoder.MaxFrameSize);
        var bufferSize = contentLength is > 0 and <= int.MaxValue
            ? (int)Math.Min(contentLength.Value, maxSize)
            : maxSize;
        var buffer = MemoryPool<byte>.Shared.Rent(Math.Max(bufferSize, 256));
        _activeBodyBuffers[streamId] = buffer;
        _activeBodyReadCts[streamId] = new CancellationTokenSource();
        ReadNextBodyChunk(streamId);
    }

    private void ReadNextBodyChunk(int streamId)
    {
        if (!_activeBodyStreams.TryGetValue(streamId, out var stream) ||
            !_activeBodyBuffers.TryGetValue(streamId, out var buffer))
        {
            return;
        }

        if (_streams.TryGetValue(streamId, out var state))
        {
            state.IsBodyReadPending = true;
        }

        var token = _activeBodyReadCts.TryGetValue(streamId, out var cts) ? cts.Token : CancellationToken.None;
        var vt = stream.ReadAsync(buffer.Memory, token);
        if (vt.IsCompletedSuccessfully)
        {
            // Completed inline on the actor thread: the buffer was never exposed across a
            // message boundary, so no in-flight tracking is needed.
            HandleStreamBodyRead(new StreamBodyReadComplete(streamId, vt.Result));
            return;
        }

        // The read is now genuinely in flight on another thread writing into the reusable
        // buffer. Mark it so CleanupBodyDrain defers buffer disposal until it completes.
        _drainReadInFlight.Add(streamId);
        vt.AsTask().PipeTo(
            _ops.StageActor,
            success: bytesRead => new StreamBodyReadComplete(streamId, bytesRead),
            failure: ex => new StreamBodyReadFailed(streamId, ex));
    }

    private void CleanupBodyDrain(int streamId)
    {
        _activeBodyStreams.Remove(streamId);

        if (_drainReadInFlight.Contains(streamId))
        {
            // A ReadAsync into the reusable drain buffer is still in flight. Returning the
            // buffer to the pool now would let a concurrent stream re-rent and overwrite it
            // mid-read. Cancel the read and defer buffer/cts disposal to its completion.
            _drainBufferOrphaned.Add(streamId);
            if (_activeBodyReadCts.TryGetValue(streamId, out var pendingCts))
            {
                pendingCts.Cancel();
            }

            return;
        }

        DisposeDrainResources(streamId);
    }

    private void DisposeDrainResources(int streamId)
    {
        if (_activeBodyBuffers.Remove(streamId, out var buffer))
        {
            buffer.Dispose();
        }

        if (_activeBodyReadCts.Remove(streamId, out var cts))
        {
            cts.Dispose();
        }

        _drainBufferOrphaned.Remove(streamId);
    }

    private void EmitFrame(Http2Frame frame)
    {
        if (frame is DataFrame d)
        {
            Tracing.For("Protocol").Trace(this, "HTTP/2: DATA out (stream={0}, len={1}, endStream={2})",
                d.StreamId, d.Data.Length, d.EndStream);
        }

        if (frame is DataFrame { Data.Length: > 0 } df)
        {
            _responseRate.Observe(df.StreamId, df.Data.Length, Now());
            EnsureRateTimer();
        }

        var totalSize = frame.SerializedSize;
        var buf = TransportBuffer.Rent(totalSize);
        var span = buf.FullMemory.Span;
        frame.WriteTo(ref span);
        buf.Length = totalSize;
        _ops.OnOutbound(TransportData.Rent(buf));
    }

    public void EmitRstStream(int streamId, Http2ErrorCode errorCode)
    {
        Tracing.For("Protocol").Debug(this, "HTTP/2: RST_STREAM (stream={0}, error={1})", streamId, errorCode);
        EmitFrame(new RstStreamFrame(streamId, errorCode));
        CloseStream(streamId);
    }

    public void EmitGoAway(int lastStreamId, Http2ErrorCode errorCode, string? reason = null)
    {
        var debugData = reason is not null
            ? Encoding.UTF8.GetBytes(reason).AsMemory()
            : ReadOnlyMemory<byte>.Empty;

        EmitFrame(new GoAwayFrame(lastStreamId, errorCode, debugData));
    }

    public void CheckDataRates()
    {
        _rateTimerActive = false;
        var now = Now();
        _rateViolations.Clear();

        _requestRate.Check(now, _rateViolations);
        _responseRate.Check(now, _rateViolations);

        _rateViolationSet.Clear();
        foreach (var violation in _rateViolations)
        {
            _rateViolationSet.Add(violation);
        }

        foreach (var streamId in _rateViolationSet)
        {
            Tracing.For("Protocol").Warning(this, "HTTP/2: data rate violation (stream={0})", streamId);
            EmitRstStream((int)streamId, Http2ErrorCode.EnhanceYourCalm);
        }

        if (_requestRate.Count > 0 || _responseRate.Count > 0)
        {
            EnsureRateTimer();
        }
    }

    private void EnsureRateTimer()
    {
        if (_rateTimerActive)
        {
            return;
        }

        _rateTimerActive = true;
        _ops.OnScheduleTimer(DataRateCheck, TimeSpan.FromSeconds(1));
    }
}