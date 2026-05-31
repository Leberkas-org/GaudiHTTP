using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Multiplexed;
using TurboHTTP.Protocol.Multiplexed.Body;
using TurboHTTP.Protocol.Server;
using TurboHTTP.Protocol.Syntax.Http2.Options;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;
using static Servus.Core.Servus;

namespace TurboHTTP.Protocol.Syntax.Http2.Server;

internal sealed class Http2ServerSessionManager
{
    private const int MaxStatePoolCapacity = 1000;

    private readonly Http2ServerEncoderOptions _encoderOptions;
    private readonly Http2ServerDecoderOptions _decoderOptions;
    private readonly IServerStageOperations _ops;
    private readonly FrameDecoder _frameDecoder = new();
    private readonly Http2ServerDecoder _requestDecoder;
    private readonly Http2ServerEncoder _responseEncoder;
    private readonly FlowController _flow;
    private readonly StreamTracker _tracker;
    private readonly long _maxRequestBodySize;
    private readonly long _maxResponseBufferSize;
    private readonly int _responseBodyChunkSize;
    private readonly TimeSpan _bodyConsumptionTimeout;
    private readonly int _initialStreamWindowSize;

    private readonly Dictionary<int, StreamState> _streams = new();
    private readonly StackStreamStatePool<StreamState> _statePool;

    private int _nextContinuationStreamId;
    private bool _continuationEndStream;
    private readonly DataRateMonitor _requestRate;
    private readonly DataRateMonitor _responseRate;
    private bool _prefaceConsumed;

    public int ActiveStreamCount => _streams.Count;
    public int MaxConcurrentStreams => _decoderOptions.MaxConcurrentStreams;

    public Http2ServerSessionManager(
        Http2ConnectionOptions options,
        IServerStageOperations ops)
    {
        _encoderOptions = options.ToEncoderOptions();
        _decoderOptions = options.ToDecoderOptions();
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));

        _responseEncoder = new Http2ServerEncoder(_encoderOptions);
        _requestDecoder = new Http2ServerDecoder(_decoderOptions);
        _flow = new FlowController(options.InitialConnectionWindowSize, options.InitialStreamWindowSize);
        _tracker = new StreamTracker(initialNextStreamId: 1, options.MaxConcurrentStreams);
        _maxRequestBodySize = options.Limits.MaxRequestBodySize;
        _maxResponseBufferSize = options.MaxResponseBufferSize;
        _responseBodyChunkSize = options.ResponseBodyChunkSize;
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
            (SettingsParameter.HeaderTableSize, (uint)_encoderOptions.HeaderTableSize),
        };

        var settingsFrame = new SettingsFrame(settingsParams, isAck: false);
        EmitFrame(settingsFrame);

        var connectionWindowIncrement = _flow.RecvConnectionWindow - 65535;
        if (connectionWindowIncrement > 0)
        {
            EmitFrame(new WindowUpdateFrame(0, connectionWindowIncrement));
        }
    }

    public void DecodeClientData(TransportBuffer buffer)
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

            case GoAwayFrame goAway:
                HandleGoAwayFrame(goAway);
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

        if (state.HasBodyDecoder && _bodyConsumptionTimeout > TimeSpan.Zero)
        {
            _ops.OnScheduleTimer(string.Concat("body-consumption:", streamId.ToString()), _bodyConsumptionTimeout);
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

        if (!hasBody)
        {
            CloseStream(streamId);
            return;
        }

        if (responseBody is not TurboHttpResponseBodyFeature turboBody)
        {
            CloseStream(streamId);
            return;
        }

        var bodyStream = turboBody.GetResponseStream();
        var encoder = BodyEncoderFactory.Create(bodyStream, contentLength, new BodyEncoderOptions { ChunkSize = _responseBodyChunkSize });
        if (encoder is null)
        {
            CloseStream(streamId);
            return;
        }

        state.InitBodyEncoder(encoder, _maxResponseBufferSize);
        state.StartBodyEncoder(bodyStream, streamId, _ops.StageActor);
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
                header.Value.FirstOrDefault() is { } value && long.TryParse(value, out var length))
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
            case StreamBodyChunk<int> chunk:
                HandleOutboundBodyChunk(chunk);
                break;

            case StreamBodyComplete<int> complete:
                HandleOutboundBodyComplete(complete.StreamId);
                break;

            case StreamBodyFailed<int>(var failedStreamId, var exception):
                Tracing.For("Protocol").Warning(this,
                    "HTTP/2: Response body encoding failed for stream {0}: {1}", failedStreamId,
                    exception.Message);
                EmitRstStream(failedStreamId, Http2ErrorCode.InternalError);
                break;
        }
    }

    private void HandleOutboundBodyChunk(StreamBodyChunk<int> chunk)
    {
        var streamId = chunk.StreamId;
        if (!_streams.TryGetValue(streamId, out var state))
        {
            chunk.Owner.Dispose();
            return;
        }

        var window = _flow.GetSendWindow(streamId);
        if (window >= chunk.Length)
        {
            EmitFrame(new DataFrame(streamId, chunk.Owner.Memory[..chunk.Length], endStream: false));
            _flow.OnDataSent(streamId, chunk.Length);
            chunk.Owner.Dispose();
            return;
        }

        state.EnqueueBodyChunk(chunk);
    }

    private void HandleOutboundBodyComplete(int streamId)
    {
        if (!_streams.TryGetValue(streamId, out var state))
        {
            return;
        }

        state.MarkBodyEncoderComplete();

        if (!state.HasPendingOutbound)
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

            CloseStream(streamId);
        }
    }

    public void DrainOutboundBuffer(int streamId)
    {
        if (!_streams.TryGetValue(streamId, out var state) || !state.HasPendingOutbound)
        {
            return;
        }

        while (state.PeekBodyChunk() is { } next)
        {
            var window = _flow.GetSendWindow(streamId);
            if (window < next.Length)
            {
                break;
            }

            state.TryDequeueBodyChunk(out var chunk);
            EmitFrame(new DataFrame(streamId, chunk!.Owner.Memory[..chunk.Length], endStream: false));
            _flow.OnDataSent(streamId, chunk.Length);
            chunk.Owner.Dispose();
        }

        if (state is { HasPendingOutbound: false, IsBodyEncoderComplete: true })
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

            CloseStream(streamId);
        }
    }

    public void Cleanup()
    {
        foreach (var (_, state) in _streams)
        {
            state.AbortBody();
        }

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

        if (!_tracker.CanOpenStream())
        {
            EmitRstStream(streamId, Http2ErrorCode.RefusedStream);
            return;
        }

        var state = GetOrCreateStreamState(streamId);

        if (headers.EndHeaders)
        {
            state.AppendHeader(headers.HeaderBlockFragment.Span);
            DecodeAndEmitRequest(streamId, state, headers.EndStream);
        }
        else
        {
            state.AppendHeader(headers.HeaderBlockFragment.Span);
            _nextContinuationStreamId = streamId;
            _continuationEndStream = headers.EndStream;
            _ops.OnScheduleTimer(string.Concat("headers-timeout:", streamId.ToString()), TimeSpan.FromSeconds(30));
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

        state.AppendHeader(continuation.HeaderBlockFragment.Span);

        if (continuation.EndHeaders)
        {
            var endStream = _continuationEndStream;
            _nextContinuationStreamId = 0;
            _continuationEndStream = false;
            _ops.OnCancelTimer(string.Concat("headers-timeout:", streamId.ToString()));
            DecodeAndEmitRequest(streamId, state, endStream);
        }
    }

    private void HandleDataFrame(DataFrame data)
    {
        var streamId = data.StreamId;

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
                EmitGoAway(0, errorCode, "Flow control violation");
            }
            else
            {
                EmitRstStream(streamId, errorCode);
            }

            return;
        }

        if (state.HasBodyDecoder)
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
                _ops.OnCancelTimer(string.Concat("body-consumption:", streamId.ToString()));
            }

            if (!data.Data.IsEmpty)
            {
                _requestRate.Observe(streamId, data.Data.Length, Environment.TickCount64);
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
            return;
        }

        var ackPing = new PingFrame(ping.Data, isAck: true);
        EmitFrame(ackPing);
    }

    private void HandleGoAwayFrame(GoAwayFrame _)
    {
        _flow.OnGoAway();
    }

    private void HandleRstStreamFrame(RstStreamFrame rst)
    {
        CloseStream(rst.StreamId);
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

            _tracker.OnStreamOpened(streamId);
            _flow.InitStreamSendWindow(streamId);

            var hasBody = !endStream;
            if (hasBody)
            {
                state.InitBodyDecoder(new StreamingBodyDecoder(_maxRequestBodySize));
                requestFeature.Body = state.GetBodyStream();
            }

            var features = FeatureCollectionFactory.Create(requestFeature, hasBody, _ops.Services,
                _ops.ConnectionFeature, _ops.TlsHandshakeFeature, _maxRequestBodySize);
            features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));

            var capturedStreamId = streamId;
            features.Set<IHttpResetFeature>(new TurboHttpResetFeature(errorCode =>
                EmitRstStream(capturedStreamId, (Http2ErrorCode)errorCode)));

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
        _streams[streamId] = state;
        return state;
    }

    private void CloseStream(int streamId)
    {
        _requestRate.Remove(streamId);
        _responseRate.Remove(streamId);
        _ops.OnCancelTimer(string.Concat("body-consumption:", streamId.ToString()));

        if (_streams.TryGetValue(streamId, out var state))
        {
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

    private void EmitFrame(Http2Frame frame)
    {
        if (frame is DataFrame df && df.Data.Length > 0)
        {
            _responseRate.Observe(df.StreamId, df.Data.Length, Environment.TickCount64);
            EnsureRateTimer();
        }

        var totalSize = frame.SerializedSize;
        var buf = TransportBuffer.Rent(totalSize);
        var span = buf.FullMemory.Span;
        frame.WriteTo(ref span);
        buf.Length = totalSize;
        _ops.OnOutbound(new TransportData(buf));
    }

    public void EmitRstStream(int streamId, Http2ErrorCode errorCode)
    {
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
        var now = Environment.TickCount64;
        var violations = new List<long>();

        _requestRate.Check(now, violations);
        _responseRate.Check(now, violations);

        var violationSet = new HashSet<long>(violations);
        foreach (var streamId in violationSet)
        {
            EmitRstStream((int)streamId, Http2ErrorCode.EnhanceYourCalm);
        }

        if (_requestRate.Count > 0 || _responseRate.Count > 0)
        {
            _ops.OnScheduleTimer("data-rate-check", TimeSpan.FromSeconds(1));
        }
    }

    private void EnsureRateTimer() => _ops.OnScheduleTimer("data-rate-check", TimeSpan.FromSeconds(1));
}