using System.Buffers;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Multiplexed;
using TurboHTTP.Protocol.Multiplexed.Body;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;
using static Servus.Core.Servus;

namespace TurboHTTP.Protocol.Syntax.Http3.Server;

internal sealed class Http3ServerSessionManager
{
    private const int MaxStatePoolCapacity = 1000;

    private readonly IServerStageOperations _ops;
    private readonly ServerStreamResolver _streamResolver = new();
    private readonly Http3ServerDecoder _requestDecoder;
    private readonly Http3ServerEncoder _responseEncoder;
    private readonly QpackTableSync _tableSync;
    private readonly Http3ServerEncoderOptions _encoderOptions;
    private readonly Http3ServerDecoderOptions _decoderOptions;
    private readonly long _maxRequestBodySize;
    private readonly BodyEncoderOptions _bodyEncoderOptions;
    private readonly TimeSpan _bodyConsumptionTimeout;

    private readonly Dictionary<long, (FrameDecoder Decoder, StreamState State)> _streams = new();
    private readonly StackStreamStatePool<StreamState> _statePool;
    private readonly DataRateMonitor _requestRate;
    private readonly DataRateMonitor _responseRate;
    private readonly TimeProvider _clock;

    private bool _controlPrefaceSent;

    private long Now() => _clock.GetUtcNow().ToUnixTimeMilliseconds();

    public int ActiveStreamCount => _streams.Count;
    public int MaxConcurrentStreams => _decoderOptions.MaxConcurrentStreams;

    public Http3ServerSessionManager(
        Http3ConnectionOptions options,
        IServerStageOperations ops,
        TimeProvider? timeProvider = null)
    {
        _clock = timeProvider ?? TimeProvider.System;
        _encoderOptions = options.ToEncoderOptions();
        _decoderOptions = options.ToDecoderOptions();
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        _maxRequestBodySize = options.Limits.MaxRequestBodySize;
        _bodyEncoderOptions = options.ToBodyEncoderOptions();
        _bodyConsumptionTimeout = options.BodyConsumptionTimeout;

        _tableSync = new QpackTableSync(
            encoderMaxCapacity: 0,
            decoderMaxCapacity: _encoderOptions.QpackMaxTableCapacity,
            maxBlockedStreams: _encoderOptions.QpackBlockedStreams,
            configuredEncoderLimit: _encoderOptions.QpackMaxTableCapacity);

        _requestDecoder = new Http3ServerDecoder(_tableSync, _decoderOptions);
        _responseEncoder = new Http3ServerEncoder(_tableSync, _encoderOptions);

        var rate = options.ToRateMonitor();
        _requestRate = new DataRateMonitor(rate.MinRequestBodyDataRate, rate.MinRequestBodyDataRateGracePeriod);
        _responseRate = new DataRateMonitor(rate.MinResponseDataRate, rate.MinResponseDataRateGracePeriod);

        var statePoolCapacity = Math.Min(
            _decoderOptions.MaxConcurrentStreams > 0 ? _decoderOptions.MaxConcurrentStreams : 100,
            MaxStatePoolCapacity);
        _statePool = new StackStreamStatePool<StreamState>(
            statePoolCapacity,
            () => new StreamState());
    }

    public void PreStart()
    {
        _ops.OnOutbound(new OpenStream(CriticalStreamId.Control, StreamDirection.Unidirectional));
        _ops.OnOutbound(new OpenStream(CriticalStreamId.QpackEncoder, StreamDirection.Unidirectional));
        _ops.OnOutbound(new OpenStream(CriticalStreamId.QpackDecoder, StreamDirection.Unidirectional));

        var preface = BuildControlPreface();
        _ops.OnOutbound(preface);
    }

    public void DecodeClientData(ITransportInbound data)
    {
        switch (data)
        {
            case ServerStreamAccepted { Id: var id }:
            {
                _streamResolver.OnServerStreamOpened(id);
                return;
            }

            case MultiplexedData multiplexed:
            {
                HandleTaggedStreamData(multiplexed);
                return;
            }

            case StreamReadCompleted { Id.Value: >= 0 } readCompleted:
            {
                FlushPendingRequest(readCompleted.Id.Value);
                return;
            }

            case StreamClosed { Id.Value: >= 0 } streamClosed:
            {
                FlushPendingRequest(streamClosed.Id.Value);
                return;
            }

            case TransportData rawData:
            {
                Tracing.For("Protocol").Warning(this,
                    "Received untagged TransportData — dropping to prevent stream ID misrouting.");
                rawData.Buffer.Dispose();
                return;
            }
        }
    }

    public void OnResponse(IFeatureCollection features)
    {
        var streamId = GetStreamIdFromFeatures(features);

        if (streamId < 0)
        {
            Tracing.For("Protocol").Warning(this, "HTTP/3 response missing stream ID");
            return;
        }

        if (!_streams.TryGetValue(streamId, out var streamData))
        {
            Tracing.For("Protocol").Warning(this, "HTTP/3: Response for unknown stream {0}", streamId);
            return;
        }

        var (_, state) = streamData;

        if (state.HasBodyDecoder && _bodyConsumptionTimeout > TimeSpan.Zero)
        {
            _ops.OnScheduleTimer(string.Concat("body-consumption:", streamId.ToString()), _bodyConsumptionTimeout);
        }

        var headersFrame = _responseEncoder.EncodeHeaders(features);
        EmitDataFrame(headersFrame, streamId);

        var responseFeature = features.Get<IHttpResponseFeature>();
        var responseBody = features.Get<IHttpResponseBodyFeature>();
        var contentLength = ExtractContentLength(responseFeature);
        var hasStarted = responseBody is TurboHttpResponseBodyFeature { HasStarted: true };
        var hasBody = contentLength is not null and not 0
                      || (contentLength is null && hasStarted);

        if (!hasBody)
        {
            _ops.OnOutbound(new CompleteWrites(streamId));
            return;
        }

        if (responseBody is not TurboHttpResponseBodyFeature turboBody)
        {
            _ops.OnOutbound(new CompleteWrites(streamId));
            return;
        }

        var bodyStream = turboBody.GetResponseStream();
        var encoder = BodyEncoderFactory.Create(bodyStream, contentLength, _bodyEncoderOptions);
        if (encoder is null)
        {
            _ops.OnOutbound(new CompleteWrites(streamId));
            return;
        }

        state.InitBodyEncoder(encoder);
        state.StartBodyEncoder(bodyStream, streamId, _ops.StageActor);
        _ops.OnScheduleTimer(string.Concat("drain-body:", streamId.ToString()), TimeSpan.FromMilliseconds(0));
    }

    private static long? ExtractContentLength(IHttpResponseFeature? responseFeature)
    {
        if (responseFeature?.Headers is null)
        {
            return null;
        }

        foreach (var header in responseFeature.Headers)
        {
            if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
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
            case StreamBodyChunk<long> chunk:
                HandleOutboundBodyChunk(chunk);
                break;

            case StreamBodyComplete<long> complete:
                HandleOutboundBodyComplete(complete.StreamId);
                break;

            case StreamBodyFailed<long> failed:
                Tracing.For("Protocol").Warning(this,
                    "HTTP/3: Response body encoding failed for stream {0}: {1}", failed.StreamId,
                    failed.Reason.Message);
                EmitRstStream(failed.StreamId, ErrorCode.GeneralProtocolError);
                break;
        }
    }

    private void HandleOutboundBodyChunk(StreamBodyChunk<long> chunk)
    {
        if (!_streams.TryGetValue(chunk.StreamId, out var streamData))
        {
            chunk.Owner.Dispose();
            return;
        }

        var (_, state) = streamData;
        state.EnqueueBodyChunk(chunk);
        DrainOutboundBuffer(chunk.StreamId);
    }

    private void HandleOutboundBodyComplete(long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var streamData))
        {
            return;
        }

        var (_, state) = streamData;
        state.MarkBodyEncoderComplete();

        if (!state.HasPendingOutbound)
        {
            _ops.OnOutbound(new CompleteWrites(streamId));
        }
    }

    public void DrainOutboundBuffer(long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var streamData))
        {
            return;
        }

        var (_, state) = streamData;

        const int maxFrameSize = 16384;

        while (state.PeekBodyChunk() is { } chunk)
        {
            var chunkSize = Math.Min(maxFrameSize, chunk.Length);
            var dataFrame = new DataFrame(chunk.Owner.Memory[..chunkSize]);

            EmitDataFrame(dataFrame, streamId);

            if (chunkSize >= chunk.Length)
            {
                state.TryDequeueBodyChunk(out _);
                chunk.Owner.Dispose();
            }
            else
            {
                break;
            }
        }

        if (state is { HasPendingOutbound: false, IsBodyEncoderComplete: true })
        {
            _ops.OnOutbound(new CompleteWrites(streamId));
        }
    }

    public void FlushAllPendingRequests()
    {
        var streamIds = _streams.Keys.ToList();
        foreach (var streamId in streamIds)
        {
            FlushPendingRequest(streamId);
        }
    }

    public void Cleanup()
    {
        foreach (var (_, (decoder, state)) in _streams)
        {
            decoder.Dispose();
            state.AbortBody();
            state.Reset();
            _statePool.Return(state);
        }

        _streams.Clear();
        _streamResolver.Reset();
        _tableSync.Reset();
    }

    public void CheckDataRates()
    {
        var now = Now();
        var violations = new List<long>();

        _requestRate.Check(now, violations);
        _responseRate.Check(now, violations);

        var violationSet = new HashSet<long>(violations);
        foreach (var streamId in violationSet)
        {
            EmitRstStream(streamId, ErrorCode.GeneralProtocolError);
        }

        if (_requestRate.Count > 0 || _responseRate.Count > 0)
        {
            _ops.OnScheduleTimer("data-rate-check", TimeSpan.FromSeconds(1));
        }
    }

    private void EnsureRateTimer() => _ops.OnScheduleTimer("data-rate-check", TimeSpan.FromSeconds(1));

    public void EmitRstStream(long streamId, ErrorCode errorCode)
    {
        _ops.OnOutbound(new ResetStream(streamId, (long)errorCode));
        CloseStream(streamId);
    }

    private void HandleTaggedStreamData(MultiplexedData multiplexed)
    {
        var (logicalStreamId, transportBuffer) = _streamResolver.Resolve(multiplexed.StreamId, multiplexed.Buffer);

        if (transportBuffer is null)
        {
            return;
        }

        if (logicalStreamId == CriticalStreamId.ControlId)
        {
            ProcessFrameData(transportBuffer, CriticalStreamId.ControlId);
            return;
        }

        if (logicalStreamId == CriticalStreamId.QpackEncoderId)
        {
            transportBuffer.Dispose();
            return;
        }

        if (logicalStreamId == CriticalStreamId.QpackDecoderId)
        {
            transportBuffer.Dispose();
            return;
        }

        ProcessFrameData(transportBuffer, logicalStreamId);
    }

    private void ProcessFrameData(TransportBuffer buffer, long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var streamData))
        {
            var frameDecoder = new FrameDecoder();
            var streamState = new StreamState();
            streamState.Initialize(streamId);
            streamData = (frameDecoder, streamState);
            _streams[streamId] = streamData;
        }

        var (decoder, state) = streamData;

        var frames = decoder.DecodeAll(buffer.Span, out _);
        buffer.Dispose();

        foreach (var frame in frames)
        {
            try
            {
                switch (frame)
                {
                    case HeadersFrame headersFrame:
                    {
                        var requestFeature =
                            _requestDecoder.DecodeHeadersToFeature(headersFrame, state, endStream: false);
                        if (requestFeature is not null)
                        {
                            state.InitRequestFeature(requestFeature);
                        }
                        else
                        {
                            _ops.OnScheduleTimer(string.Concat("headers-timeout:", streamId.ToString()),
                                TimeSpan.FromSeconds(30));
                        }

                        break;
                    }

                    case DataFrame dataFrame:
                    {
                        HandleDataFrame(dataFrame, streamId, state);
                        break;
                    }

                    case SettingsFrame:
                    case GoAwayFrame:
                    {
                        break;
                    }
                }
            }
            catch (HttpProtocolException ex)
            {
                Tracing.For("Protocol").Warning(this,
                    "HTTP/3 frame processing error on stream {0}: {1}", streamId, ex.Message);
            }
        }
    }

    private void FlushPendingRequest(long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var streamData))
        {
            return;
        }

        var (_, state) = streamData;

        var requestFeature = state.GetRequestFeature();
        if (requestFeature is not null)
        {
            _ops.OnCancelTimer(string.Concat("headers-timeout:", streamId.ToString()));
            _ops.OnCancelTimer(string.Concat("body-consumption:", streamId.ToString()));

            var hasBody = state.HasBodyDecoder;
            if (hasBody)
            {
                state.FeedBody(ReadOnlySpan<byte>.Empty, endStream: true);
                requestFeature.Body = state.GetBodyStream();
            }

            var features = FeatureCollectionFactory.Create(requestFeature, hasBody, _ops.Services,
                _ops.ConnectionFeature, _ops.TlsHandshakeFeature, _maxRequestBodySize);
            features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));

            var capturedStreamId = streamId;
            features.Set<IHttpResetFeature>(new TurboHttpResetFeature(errorCode =>
                EmitRstStream(capturedStreamId, (ErrorCode)errorCode)));

            _ops.OnRequest(features);
        }
    }

    private void HandleDataFrame(DataFrame dataFrame, long streamId, StreamState state)
    {
        if (!state.HasBodyDecoder)
        {
            state.InitBodyDecoder(new StreamingBodyDecoder(_maxRequestBodySize));
        }

        try
        {
            state.FeedBody(dataFrame.Data.Span, endStream: false);
        }
        catch (HttpProtocolException)
        {
            state.AbortBody();
            EmitRstStream(streamId, ErrorCode.GeneralProtocolError);
            return;
        }

        if (!dataFrame.Data.IsEmpty)
        {
            _requestRate.Observe(streamId, dataFrame.Data.Length, Now());
            EnsureRateTimer();
        }
    }

    private long GetStreamIdFromFeatures(IFeatureCollection features)
    {
        var streamIdFeature = features.Get<IHttpStreamIdFeature>();
        if (streamIdFeature is not null)
        {
            return streamIdFeature.StreamId;
        }

        return -1L;
    }

    private void CloseStream(long streamId)
    {
        _requestRate.Remove(streamId);
        _responseRate.Remove(streamId);
        _ops.OnCancelTimer(string.Concat("body-consumption:", streamId.ToString()));

        if (_streams.TryGetValue(streamId, out var streamData))
        {
            var (decoder, state) = streamData;

            decoder.Dispose();
            state.Reset();
            _statePool.Return(state);

            _streams.Remove(streamId);
        }
    }

    private void EmitDataFrame(object frame, long streamId)
    {
        var serialized = frame switch
        {
            HeadersFrame hf => hf.SerializedSize,
            DataFrame df => df.SerializedSize,
            _ => 0
        };

        var buf = TransportBuffer.Rent(serialized);
        var span = buf.FullMemory.Span;

        switch (frame)
        {
            case HeadersFrame hf:
                hf.WriteTo(ref span);
                break;
            case DataFrame df:
                df.WriteTo(ref span);
                if (df.Data.Length > 0)
                {
                    _responseRate.Observe(streamId, df.Data.Length, Now());
                    EnsureRateTimer();
                }

                break;
        }

        buf.Length = serialized;
        _ops.OnOutbound(new MultiplexedData(buf, streamId));
    }

    private MultiplexedData BuildControlPreface()
    {
        if (_controlPrefaceSent)
        {
            throw new InvalidOperationException("Control preface already sent");
        }

        _controlPrefaceSent = true;

        var settings = new Settings();
        settings.Set(SettingsIdentifier.QpackMaxTableCapacity, _encoderOptions.QpackMaxTableCapacity);
        settings.Set(SettingsIdentifier.QpackBlockedStreams, _encoderOptions.QpackBlockedStreams);
        var settingsFrame = settings.ToFrame();

        var streamTypeSize = QuicVarInt.EncodedLength((long)StreamType.Control);
        var frameSize = settingsFrame.SerializedSize;
        var totalSize = streamTypeSize + frameSize;

        using var owner = MemoryPool<byte>.Shared.Rent(totalSize);
        var span = owner.Memory.Span;

        var written = QuicVarInt.Encode((long)StreamType.Control, span);
        span = span[written..];
        settingsFrame.WriteTo(ref span);

        var buf = TransportBuffer.Rent(totalSize);
        owner.Memory.Span[..totalSize].CopyTo(buf.FullMemory.Span);
        buf.Length = totalSize;

        return new MultiplexedData(buf, CriticalStreamId.Control);
    }
}