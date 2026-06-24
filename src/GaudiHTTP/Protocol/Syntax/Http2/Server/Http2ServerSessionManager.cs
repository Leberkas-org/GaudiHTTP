using System.Runtime.InteropServices;
using System.Text;
using Akka.Actor;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Pooling;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Multiplexed;
using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Syntax.Http2.Options;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Streams.Stages.Server;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Syntax.Http2.Server;

internal sealed class Http2ServerSessionManager : IBodyDrainTarget<int>
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
    private readonly Dictionary<int, int> _deferredStreamIncrements = new();

    internal readonly record struct StreamBodyConsumed(int StreamId);

    private readonly ConnectionPoolContext _poolContext = new();
    private readonly CancellationTokenSource _connectionCts = new();
    private FlowControlledBodyPump? _pump;

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
        _pump = new FlowControlledBodyPump(this, _flow, _poolContext, _connectionCts);
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

            // Decode returns the decoder's reused frame list; iterate it synchronously here within
            // the same actor message and never retain it (Akka back-pressure guarantees consumption).
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

    private void SendInformational(int streamId, int statusCode, IHeaderDictionary headers)
    {
        var fc = new GaudiFeatureCollection();
        var responseFeature = new GaudiHttpResponseFeature { StatusCode = statusCode };
        foreach (var h in headers)
        {
            responseFeature.Headers[h.Key] = h.Value;
        }

        fc.Set<IHttpResponseFeature>(responseFeature);
        fc.Set<IHttpStreamIdFeature>(new GaudiStreamIdFeature(streamId));

        var frames = _responseEncoder.EncodeHeaders(fc, streamId, hasBody: true);
        for (var i = 0; i < frames.Count; i++)
        {
            EmitFrame(frames[i]);
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
        // A response to HEAD keeps its headers (incl. content-length) but carries no DATA — the
        // HEADERS frame ends the stream (RFC 9113 §8.1). Treating it as body-less routes it through
        // the tested END_STREAM-on-HEADERS / CloseStream path and suppresses every DATA frame.
        var isHead = string.Equals(
            features.Get<IHttpRequestFeature>()?.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
        var hasBody = !isHead
                      && (contentLength is not null and not 0
                          || (contentLength is null && responseBody is GaudiHttpResponseBodyFeature
                          {
                              HasStarted: true
                          }));

        var frames = _responseEncoder.EncodeHeaders(features, streamId, hasBody);
        for (var i = 0; i < frames.Count; i++)
        {
            EmitFrame(frames[i]);
        }

        if (!hasBody || responseBody is not GaudiHttpResponseBodyFeature gaudiBody)
        {
            CloseStream(streamId);
            return;
        }

        if (gaudiBody.TryGetBufferedBody(out var bufferedBody))
        {
            if (bufferedBody.Length > 0)
            {
                var window = _flow.GetSendWindow(streamId);
                if (window >= bufferedBody.Length)
                {
                    var bufferedFeatures = state.GetFeatures();
                    var trailerFeature = bufferedFeatures?.Get<IHttpResponseTrailersFeature>();
                    var hasTrailers = trailerFeature?.Trailers.Count > 0;

                    if (hasTrailers)
                    {
                        if (trailerFeature!.Trailers is GaudiHeaderDictionary gaudiTrailers)
                        {
                            gaudiTrailers.SetReadOnly();
                        }

                        var trailerFrames = _responseEncoder.EncodeTrailers(streamId, trailerFeature.Trailers);
                        if (trailerFrames.Count > 0)
                        {
                            EmitBufferedDataFrames(streamId, bufferedBody, endStream: false);
                            _flow.OnDataSent(streamId, bufferedBody.Length);
                            for (var i = 0; i < trailerFrames.Count; i++)
                            {
                                EmitFrame(trailerFrames[i]);
                            }
                        }
                        else
                        {
                            EmitBufferedDataFrames(streamId, bufferedBody, endStream: true);
                            _flow.OnDataSent(streamId, bufferedBody.Length);
                        }
                    }
                    else
                    {
                        EmitBufferedDataFrames(streamId, bufferedBody, endStream: true);
                        _flow.OnDataSent(streamId, bufferedBody.Length);
                    }
                    CloseStream(streamId);
                    return;
                }

                // When the send window is exhausted, avoid copying the entire buffered body
                // into MemoryPool chunks. Under high concurrency (256+ streams), that creates
                // hundreds of MB of Gen-2 garbage per round. If the memory is array-backed,
                // wrap it zero-copy and use the async drain path that reads in small chunks.
                if (window <= 0
                    && MemoryMarshal.TryGetArray(bufferedBody, out var segment))
                {
                    state.MarkBodyDrainActive();
                    _pump!.Register(streamId,
                        new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false),
                        CancellationToken.None, initialCredits: 16);
                    return;
                }

                SendBufferedBodyWithFlowControl(streamId, state, bufferedBody, window);
                return;
            }
            else
            {
                EmitEndOfBody(streamId, state);
                CloseStream(streamId);
                return;
            }
        }

        var bodyStream = gaudiBody.GetResponseStream();
        state.MarkBodyDrainActive();
        _pump!.Register(streamId, bodyStream, CancellationToken.None, initialCredits: 16);
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
            case DrainReadComplete<int> read:
                _pump?.HandleReadComplete(read.StreamId, read.BytesRead);
                break;

            case DrainReadFailed<int> failed:
                _pump?.HandleReadFailed(failed.StreamId, failed.Reason);
                break;

            case StreamBodyConsumed consumed:
                if (_deferredStreamIncrements.TryGetValue(consumed.StreamId, out var inc) && inc > 0)
                {
                    EmitFrame(new WindowUpdateFrame(consumed.StreamId, inc));
                    _deferredStreamIncrements.Remove(consumed.StreamId);
                }

                break;
        }
    }

    private void EmitEndOfBody(int streamId, StreamState state)
    {
        var features = state.GetFeatures();
        var trailerFeature = features?.Get<IHttpResponseTrailersFeature>();
        var hasTrailers = trailerFeature?.Trailers.Count > 0;

        if (hasTrailers)
        {
            if (trailerFeature!.Trailers is GaudiHeaderDictionary gaudiTrailers)
            {
                gaudiTrailers.SetReadOnly();
            }

            var trailerFrames = _responseEncoder.EncodeTrailers(streamId, trailerFeature.Trailers);
            if (trailerFrames.Count > 0)
            {
                EmitFrame(new DataFrame(streamId, ReadOnlyMemory<byte>.Empty, endStream: false));
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
        else
        {
            EmitFrame(new DataFrame(streamId, ReadOnlyMemory<byte>.Empty, endStream: true));
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

        _pump?.Cleanup();
        _frameDecoder.Dispose();

        foreach (var state in _streams.Values)
        {
            ReturnBodyReader(state);
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
            state.AppendHeader(headers.HeaderBlockFragment.Span, _decoderOptions.MaxHeaderBytes);
            if (isTrailer)
            {
                HandleTrailers(streamId, state);
            }
            else
            {
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

        if (state.IsRemoteClosed)
        {
            // RFC 9113 §5.1: a stream in half-closed(remote) MUST treat any DATA as a
            // STREAM_CLOSED stream error — the client already sent END_STREAM.
            EmitRstStream(streamId, Http2ErrorCode.StreamClosed);
            return;
        }

        var flowResult = _flow.OnInboundData(streamId, data.FlowControlledLength);

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
                Tracing.For("Protocol")
                    .Warning(this, "HTTP/2: stream-level flow control violation (stream={0})", streamId);
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

        if (data.EndStream)
        {
            // RFC 9113 §5.1: the client has finished sending; further DATA must be rejected.
            state.MarkRemoteClosed();
        }

        if (flowResult.StreamWindowUpdate is { } streamWin)
        {
            _deferredStreamIncrements.TryGetValue(streamId, out var existing);
            _deferredStreamIncrements[streamId] = existing + streamWin.Increment;
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

        // A change in INITIAL_WINDOW_SIZE adjusts all stream send windows. Notify the
        // scheduler so it can unblock window-blocked streams. Stream-id 0 triggers
        // connection-level re-evaluation which covers all active drains.
        if (result.InitialWindowSizeChange.HasValue)
        {
            _pump?.OnWindowUpdate(0);
        }
    }

    private void HandleWindowUpdateFrame(WindowUpdateFrame windowUpdate)
    {
        _flow.OnSendWindowUpdate(windowUpdate.StreamId, windowUpdate.Increment);
        _pump?.OnWindowUpdate(windowUpdate.StreamId);
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
        Tracing.For("Protocol").Debug(this, "HTTP/2: received RST_STREAM (stream={0}, error={1})", rst.StreamId,
            rst.ErrorCode);
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
        // RFC 9113 §5.1: trailers carry END_STREAM — the stream is now half-closed(remote).
        state.MarkRemoteClosed();
    }

    private void DecodeAndEmitRequest(int streamId, StreamState state, bool endStream)
    {
        try
        {
            var hasBody = !endStream;
            var features = FeatureCollectionFactory.Create(_ops.PoolContext!, hasBody,
                out var requestFeature, _ops.ConnectionFeature,
                _ops.TlsHandshakeFeature, _maxRequestBodySize);

            _requestDecoder.PopulateRequestFeature(streamId, state, requestFeature);

            _flow.InitStreamSendWindow(streamId);

            if (endStream)
            {
                state.MarkRemoteClosed();
            }

            if (hasBody)
            {
                var queued = _ops.PoolContext!.Rent(() => new QueuedBodyReader(capacity: 8));
                state.InitBodyReader(queued, _maxRequestBodySize);
                requestFeature.Body = state.GetBodyStream();

                var capturedBodyStreamId = streamId;
                queued.SlotFreed += () =>
                    _ops.StageActor.Tell(new StreamBodyConsumed(capturedBodyStreamId), ActorRefs.NoSender);
            }

            features.Set<IHttpStreamIdFeature>(new GaudiStreamIdFeature(streamId));

            var capturedStreamId = streamId;
            features.Set<IHttpResetFeature>(new GaudiHttpResetFeature(errorCode =>
                EmitRstStream(capturedStreamId, (Http2ErrorCode)errorCode)));
            features.Set(new GaudiInformationalResponseFeature((statusCode, headers) =>
                SendInformational(capturedStreamId, statusCode, headers)));

            Tracing.For("Protocol")
                .Debug(this, "HTTP/2: request dispatched (stream={0}, hasBody={1})", streamId, hasBody);
            _ops.OnRequest(features);

            if (string.Equals(requestFeature.Headers[WellKnownHeaders.Expect], "100-continue",
                    StringComparison.OrdinalIgnoreCase))
            {
                SendInformational(capturedStreamId, 100, new HeaderDictionary());
            }
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
        _deferredStreamIncrements.Remove(streamId);
        _pump?.Cancel(streamId);

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

            ReturnBodyReader(state);
            state.Reset();
            _statePool.Return(state);

            _streams.Remove(streamId);
        }
    }

    private void SendBufferedBodyWithFlowControl(int streamId, StreamState state, ReadOnlyMemory<byte> body,
        long window)
    {
        var sent = 0;

        if (window > 0)
        {
            sent = (int)Math.Min(window, body.Length);
            var sendable = body[..sent];
            EmitBufferedDataFrames(streamId, sendable, endStream: false);
            _flow.OnDataSent(streamId, sent);
        }

        var remainder = body[sent..];
        if (remainder.Length == 0)
        {
            EmitEndOfBody(streamId, state);
            CloseStream(streamId);
            return;
        }

        // Hand the remainder to the scheduler which will emit it when WINDOW_UPDATE arrives.
        state.MarkBodyDrainActive();
        var remainderBytes = remainder.ToArray();
        _pump!.Register(streamId, new MemoryStream(remainderBytes, writable: false), CancellationToken.None, initialCredits: 16);

        Tracing.For("Protocol").Debug(this,
            "HTTP/2: buffered body flow-controlled (stream={0}, sent={1}, queued={2})",
            streamId, sent, body.Length - sent);
    }

    public void OnOutboundFlushed()
    {
        _pump?.AddCredit();
    }

    IActorRef IBodyDrainTarget<int>.PipeToTarget => _ops.StageActor;
    bool IBodyDrainTarget<int>.HasPendingDemand => _ops.HasPendingDemand;
    int IBodyDrainTarget<int>.PreferredChunkSize => _responseEncoder.MaxFrameSize;

    void IBodyDrainTarget<int>.EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
    {
        if (data.IsEmpty)
        {
            return;
        }

        EmitBufferedDataFrames(streamId, data, endStream: false);

        if (!endStream)
        {
            _pump?.AddCredit();
        }
    }

    void IBodyDrainTarget<int>.OnDrainComplete(int streamId)
    {
        Tracing.For("Protocol").Debug(this, "HTTP/2: response body complete (stream={0})", streamId);

        if (_streams.TryGetValue(streamId, out var state))
        {
            state.MarkBodyDrainComplete();
            EmitEndOfBody(streamId, state);
        }

        CloseStream(streamId);
    }

    void IBodyDrainTarget<int>.OnDrainFailed(int streamId, Exception reason)
    {
        Tracing.For("Protocol").Warning(this,
            "HTTP/2: Response body drain failed for stream {0}: {1}", streamId, reason.Message);
        EmitRstStream(streamId, Http2ErrorCode.InternalError);
    }

    private void EmitBufferedDataFrames(int streamId, ReadOnlyMemory<byte> body, bool endStream)
    {
        const int headerSize = 9;
        var maxFrame = _responseEncoder.MaxFrameSize;
        var frameCount = (body.Length + maxFrame - 1) / maxFrame;
        var totalWireSize = body.Length + frameCount * headerSize;

        var buf = TransportBuffer.Rent(totalWireSize);
        var dest = buf.FullMemory.Span;
        var offset = 0;
        var remaining = body;
        var rateActive = false;

        while (remaining.Length > maxFrame)
        {
            var chunk = remaining[..maxFrame];
            DataFrame.WriteHeaderInPlace(dest, offset, streamId, maxFrame, endStream: false);
            chunk.Span.CopyTo(dest[(offset + headerSize)..]);
            offset += headerSize + maxFrame;
            remaining = remaining[maxFrame..];

            Tracing.For("Protocol").Trace(this, "HTTP/2: DATA out (stream={0}, len={1}, endStream={2})",
                streamId, maxFrame, false);
            rateActive = true;
        }

        var lastLen = remaining.Length;
        DataFrame.WriteHeaderInPlace(dest, offset, streamId, lastLen, endStream);
        remaining.Span.CopyTo(dest[(offset + headerSize)..]);
        offset += headerSize + lastLen;

        Tracing.For("Protocol").Trace(this, "HTTP/2: DATA out (stream={0}, len={1}, endStream={2})",
            streamId, lastLen, endStream);
        if (lastLen > 0)
        {
            rateActive = true;
        }

        if (rateActive)
        {
            _responseRate.Observe(streamId, body.Length, Now());
            EnsureRateTimer();
        }

        buf.Length = offset;
        _ops.OnOutbound(TransportData.Rent(buf));
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

    private void ReturnBodyReader(StreamState state)
    {
        var reader = state.TakeBodyReader();
        if (reader is QueuedBodyReader queued)
        {
            queued.ClearSlotFreed();
            _ops.PoolContext!.Return(queued);
        }
        else
        {
            reader?.Dispose();
        }
    }
}