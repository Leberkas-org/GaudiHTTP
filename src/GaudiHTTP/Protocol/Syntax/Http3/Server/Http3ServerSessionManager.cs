using System.Buffers;
using Akka.Actor;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Pooling;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Multiplexed;
using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Protocol.Syntax.Http3.Options;
using GaudiHTTP.Protocol.Syntax.Http3.Qpack;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Streams.Stages.Server;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Syntax.Http3.Server;

internal sealed class Http3ServerSessionManager : IMultiplexedBodyDrainTarget
{
    private const int MaxStatePoolCapacity = 1000;

    // RFC 9114 §8.1 / CVE-2023-44487 (Rapid Reset): client-initiated stream aborts are counted within
    // this sliding window; exceeding the configured budget closes the connection (H3_EXCESSIVE_LOAD).
    private const long ResetWindowMs = 30_000;

    private const string DataRateCheck = "data-rate-check";

    private readonly IServerStageOperations _ops;
    private readonly ServerStreamResolver _streamResolver = new();
    private readonly Http3ServerDecoder _requestDecoder;
    private readonly Http3ServerEncoder _responseEncoder;
    private readonly QpackTableSync _tableSync;
    private readonly Http3ServerEncoderOptions _encoderOptions;
    private readonly Http3ServerDecoderOptions _decoderOptions;
    private readonly long _maxRequestBodySize;
    private readonly int _responseBodyChunkSize;
    private readonly TimeSpan _bodyConsumptionTimeout;

    private readonly Dictionary<long, (FrameDecoder Decoder, StreamState State)> _streams = new();
    private readonly CancellationTokenSource _connectionCts = new();
    private readonly ConnectionObjectPool _poolContext = new();
    private MultiplexedBodyPump? _pump;
    private readonly DataRateMonitor _requestRate;
    private readonly DataRateMonitor _responseRate;
    private readonly List<long> _rateViolations = [];
    private readonly HashSet<long> _rateViolationSet = [];
    private bool _rateTimerActive;
    private readonly TimeProvider _clock;

    private bool _controlPrefaceSent;
    private bool _settingsReceived;
    private bool _qpackDecoderPrefaceSent;

    private readonly int _maxResetStreamsPerWindow;
    private int _resetCount;
    private long _resetWindowStart;

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
        _maxResetStreamsPerWindow = options.Limits.MaxResetStreamsPerWindow;
        _responseBodyChunkSize = options.ToBodyEncoderOptions().ChunkSize;
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
    }

    public void PreStart()
    {
        _ops.OnOutbound(new OpenStream(CriticalStreamId.Control, StreamDirection.Unidirectional));
        _ops.OnOutbound(new OpenStream(CriticalStreamId.QpackEncoder, StreamDirection.Unidirectional));
        _ops.OnOutbound(new OpenStream(CriticalStreamId.QpackDecoder, StreamDirection.Unidirectional));

        var preface = BuildControlPreface();
        _ops.OnOutbound(preface);
    }

    /// <summary>
    /// True once a connection-fatal H3/QPACK error has occurred. The owning state machine surfaces
    /// this so the connection stage closes the QUIC connection rather than continuing against a
    /// desynchronized decoder.
    /// </summary>
    public bool ShouldComplete { get; private set; }

    public void SetComplete() => ShouldComplete = true;

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
                    multiplexed.Return();
                    return;
                }

            case StreamReadCompleted { Id.Value: >= 0 } readCompleted:
                {
                    FlushPendingRequest(readCompleted.Id.Value);
                    return;
                }

            case StreamClosed { Id.Value: >= 0 } streamClosed:
                {
                    if (streamClosed.Reason == DisconnectReason.Error)
                    {
                        TrackStreamReset();
                    }

                    FlushPendingRequest(streamClosed.Id.Value);
                    return;
                }

            case TransportData rawData:
                {
                    Tracing.For("Protocol").Warning(this,
                        "Received untagged TransportData - dropping to prevent stream ID misrouting.");
                    rawData.Buffer.Dispose();
                    return;
                }
        }
    }

    private void SendInformational(long streamId, int statusCode, IHeaderDictionary headers)
    {
        var fc = new GaudiFeatureCollection();
        var responseFeature = new GaudiHttpResponseFeature { StatusCode = statusCode };
        foreach (var h in headers)
        {
            responseFeature.Headers[h.Key] = h.Value;
        }

        fc.Set<IHttpResponseFeature>(responseFeature);

        var headersFrame = _responseEncoder.EncodeHeaders(fc);
        EmitDataFrame(headersFrame, streamId);
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

        state.SetFeatures(features);

        if (state.HasBodyReader && _bodyConsumptionTimeout > TimeSpan.Zero)
        {
            _ops.OnScheduleTimer(state.BodyConsumptionTimerKey, _bodyConsumptionTimeout);
        }

        var headersFrame = _responseEncoder.EncodeHeaders(features);
        EmitDataFrame(headersFrame, streamId);

        var responseFeature = features.Get<IHttpResponseFeature>();
        var responseBody = features.Get<IHttpResponseBodyFeature>();
        var contentLength = ExtractContentLength(responseFeature);
        var hasStarted = responseBody is GaudiHttpResponseBodyFeature { HasStarted: true };
        var hasBody = contentLength is not null and not 0
                      || (contentLength is null && hasStarted);

        if (!hasBody || responseBody is not GaudiHttpResponseBodyFeature gaudiBody)
        {
            EmitEndOfBody(streamId, state);
            return;
        }

        if (gaudiBody.TryGetBufferedBody(out var bufferedBody))
        {
            if (bufferedBody.Length > 0)
            {
                EmitBufferedDataFrames(streamId, bufferedBody, endStream: true);
            }

            EmitEndOfBody(streamId, state);
            CloseStream(streamId);
            return;
        }

        var bodyStream = gaudiBody.GetResponseStream();
        state.MarkBodyDrainActive();
        _pump ??= new MultiplexedBodyPump(this, _connectionCts, _poolContext, 16 * 1024);
        _pump.Register(streamId, bodyStream, contentLength: null, CancellationToken.None);
        Tracing.For("Protocol").Debug(this, "HTTP/3: response body drain started (stream={0})", streamId);
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
            case BodyReadContinue<long> cont:
                _pump?.HandleBodyReadContinue(cont.StreamId);
                break;

            case BodyReadComplete<long> read:
                _pump?.HandleReadComplete(read.StreamId, read.BytesRead);
                break;

            case BodyReadFailed<long> failed:
                _pump?.HandleReadFailed(failed.StreamId, failed.Reason);
                break;
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
        _pump?.Cleanup();

        foreach (var (_, (decoder, state)) in _streams)
        {
            ReturnDecoder(decoder);
            state.AbortBody();
            ReturnBodyReader(state);
            state.Reset();
            _poolContext.Return(state);
        }

        _streams.Clear();
        _streamResolver.Reset();
        _tableSync.Reset();
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
            Tracing.For("Protocol").Warning(this, "HTTP/3: data rate violation (stream={0})", streamId);
            EmitRstStream(streamId, ErrorCode.GeneralProtocolError);
        }

        if (_requestRate.Count > 0 || _responseRate.Count > 0)
        {
            EnsureRateTimer();
        }
    }


    public void EmitRstStream(long streamId, ErrorCode errorCode)
    {
        Tracing.For("Protocol").Debug(this, "HTTP/3: RST_STREAM (stream={0}, error={1})", streamId, errorCode);
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

        switch (logicalStreamId)
        {
            case CriticalStreamId.ControlId:
                ProcessFrameData(transportBuffer, CriticalStreamId.ControlId);
                return;
            case CriticalStreamId.QpackEncoderId:
                ProcessQpackEncoderStream(transportBuffer);
                return;
            case CriticalStreamId.QpackDecoderId:
                ProcessQpackDecoderStream(transportBuffer);
                return;
            default:
                ProcessFrameData(transportBuffer, logicalStreamId);
                break;
        }
    }

    private void ProcessQpackEncoderStream(TransportBuffer buffer)
    {
        using var input = buffer;

        try
        {
            // RFC 9204 §4.3: apply the peer's dynamic-table insert instructions to our decoder.
            _tableSync.ProcessEncoderInstructions(input.Memory.Span);
        }
        catch (Exception ex) when (ex is QpackException or HuffmanException)
        {
            Tracing.For("Protocol").Warning(this,
                "HTTP/3 QPACK encoder-stream error - closing connection: {0}", ex.Message);
            ShouldComplete = true;
            return;
        }

        // The inserts may have unblocked request streams whose HEADERS were waiting on the
        // dynamic table. Redrive each so the request finally dispatches.
        foreach (var (streamId, headers) in _tableSync.ResolveBlockedStreams())
        {
            RedriveBlockedRequest(streamId, headers);
        }

        // RFC 9204 §4.4.3: tell the peer's encoder how far our dynamic table advanced.
        FlushInsertCountIncrement();
    }

    private void ProcessQpackDecoderStream(TransportBuffer buffer)
    {
        using var input = buffer;

        try
        {
            // RFC 9204 §4.4: the peer's decoder acks our (currently empty) encoder stream.
            _tableSync.ProcessDecoderInstructions(input.Memory.Span);
        }
        catch (Exception ex) when (ex is QpackException or HuffmanException)
        {
            Tracing.For("Protocol").Warning(this,
                "HTTP/3 QPACK decoder-stream error - closing connection: {0}", ex.Message);
            ShouldComplete = true;
        }
    }

    private void RedriveBlockedRequest(long streamId, IReadOnlyList<(string Name, string Value)> headers)
    {
        if (!_streams.TryGetValue(streamId, out var streamData))
        {
            return;
        }

        var (_, state) = streamData;
        state.IsHeadersBlocked = false;

        try
        {
            _requestDecoder.AssembleHeadersToFeature(headers, state, endStream: false);
        }
        catch (Exception ex) when (ex is QpackException or HuffmanException)
        {
            Tracing.For("Protocol").Warning(this,
                "HTTP/3 QPACK error resolving blocked stream {0} - closing connection: {1}", streamId, ex.Message);
            ShouldComplete = true;
            return;
        }
        catch (HttpProtocolException ex)
        {
            Tracing.For("Protocol").Warning(this,
                "HTTP/3 message error resolving blocked stream {0} - resetting stream: {1}", streamId, ex.Message);
            EmitRstStream(streamId, ErrorCode.MessageError);
            return;
        }

        // If the request's FIN already arrived while blocked, dispatch it now.
        if (state.PendingEndStream)
        {
            state.PendingEndStream = false;
            FlushPendingRequest(streamId);
        }
    }

    private void FlushInsertCountIncrement()
    {
        var buf = TransportBuffer.Rent(1 + 8);
        var dest = buf.FullMemory.Span;
        var offset = 0;

        if (!_qpackDecoderPrefaceSent)
        {
            dest[offset++] = (byte)StreamType.QpackDecoder;
        }

        var writer = SpanWriter.Create(dest[offset..]);
        _tableSync.WriteInsertCountIncrement(ref writer);
        offset += writer.BytesWritten;

        if (offset == 0 || (offset == 1 && !_qpackDecoderPrefaceSent))
        {
            // Nothing to acknowledge yet (no inserts applied); don't send a lone stream-type byte.
            buf.Dispose();
            return;
        }

        _qpackDecoderPrefaceSent = true;
        buf.Length = offset;
        _ops.OnOutbound(MultiplexedData.Rent(buf, CriticalStreamId.QpackDecoder));
    }

    private void ProcessFrameData(TransportBuffer buffer, long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var streamData))
        {
            var frameDecoder = RentDecoder();
            var streamState = _poolContext.Rent(() => new StreamState());
            streamState.Initialize(streamId);
            streamData = (frameDecoder, streamState);
            _streams[streamId] = streamData;
        }

        var (decoder, state) = streamData;

        // Decoded DATA/HEADERS frames slice the input buffer (zero-copy), so the buffer
        // must stay alive until the frame loop below has handled (and copied) everything.
        using var inputBuffer = buffer;

        IReadOnlyList<Http3Frame> frames;
        try
        {
            frames = decoder.DecodeAll(inputBuffer.Memory, out _);
        }
        catch (Exception ex) when (ex is HttpProtocolException or QpackException or HuffmanException)
        {
            Tracing.For("Protocol").Warning(this,
                "HTTP/3 connection framing error on stream {0} - closing connection: {1}", streamId, ex.Message);
            ShouldComplete = true;
            return;
        }

        foreach (var frame in frames)
        {
            try
            {
                switch (frame)
                {
                    case HeadersFrame headersFrame:
                        {
                            if (state.GetRequestFeature() is not null)
                            {
                                _requestDecoder.DecodeTrailers(headersFrame, state);
                                state.FeedBody([], endStream: true);
                            }
                            else
                            {
                                var requestFeature =
                                    _requestDecoder.DecodeHeadersToFeature(headersFrame, state, endStream: false);
                                if (requestFeature is not null)
                                {
                                    state.InitRequestFeature(requestFeature);
                                }
                                else
                                {
                                    if (state.GetRequestFeature() is null)
                                    {
                                        // QPACK-blocked: the header block is queued in the table sync
                                        // awaiting encoder-stream instructions. Mark it so the FIN is
                                        // deferred and ProcessQpackEncoderStream redrives dispatch.
                                        state.IsHeadersBlocked = true;
                                    }

                                    _ops.OnScheduleTimer(state.HeadersTimeoutTimerKey, TimeSpan.FromSeconds(30));
                                }
                            }

                            break;
                        }

                    case DataFrame dataFrame:
                        {
                            HandleDataFrame(dataFrame, streamId, state);
                            break;
                        }

                    case SettingsFrame settings:
                        {
                            HandleSettingsFrame(settings);
                            break;
                        }

                    case GoAwayFrame:
                        {
                            break;
                        }
                }
            }
            catch (QpackException ex)
            {
                Tracing.For("Protocol").Warning(this,
                    "HTTP/3 QPACK error on stream {0} - closing connection: {1}", streamId, ex.Message);
                ShouldComplete = true;
                return;
            }
            catch (HuffmanException ex)
            {
                Tracing.For("Protocol").Warning(this,
                    "HTTP/3 Huffman error on stream {0} - closing connection: {1}", streamId, ex.Message);
                ShouldComplete = true;
                return;
            }
            catch (HttpProtocolException ex)
            {
                Tracing.For("Protocol").Warning(this,
                    "HTTP/3 message error on stream {0} - resetting stream: {1}", streamId, ex.Message);
                EmitRstStream(streamId, ErrorCode.MessageError);
            }
            finally
            {
                // DATA/HEADERS frames own a pooled rental; handling copies what it keeps
                // (body bytes into the body reader, header strings via QPACK decode), so the
                // rental must go back to the pool here or every upload allocates fresh arrays.
                (frame as IDisposable)?.Dispose();
            }
        }
    }

    private void HandleSettingsFrame(SettingsFrame settings)
    {
        if (_settingsReceived)
        {
            Tracing.For("Protocol").Warning(this,
                "HTTP/3 RFC 9114 §7.2.4: duplicate SETTINGS frame on control stream - closing connection.");
            ShouldComplete = true;
            return;
        }

        _settingsReceived = true;
    }

    /// <summary>
    /// RFC 9114 §8.1 / CVE-2023-44487: counts client-initiated stream aborts within a sliding window. A
    /// client that opens-and-resets request streams faster than the configured budget is cut off
    /// (H3_EXCESSIVE_LOAD) - MaxConcurrentStreams alone never saturates under this attack.
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
            Tracing.For("Protocol").Warning(this,
                "HTTP/3 RFC 9114 §8.1 / CVE-2023-44487: excessive stream resets - closing connection (ExcessiveLoad).");
            ShouldComplete = true;
        }
    }

    private void FlushPendingRequest(long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var streamData))
        {
            return;
        }

        var (_, state) = streamData;

        if (state.IsHeadersBlocked)
        {
            // FIN arrived while still QPACK-blocked; defer dispatch until the encoder stream
            // resolves the headers (ProcessQpackEncoderStream -> RedriveBlockedRequest).
            state.PendingEndStream = true;
            return;
        }

        var requestFeature = state.GetRequestFeature();
        if (requestFeature is not null)
        {
            _ops.OnCancelTimer(state.HeadersTimeoutTimerKey);
            _ops.OnCancelTimer(state.BodyConsumptionTimerKey);

            var hasBody = state.HasBodyReader;
            if (hasBody)
            {
                state.FeedBody(ReadOnlySpan<byte>.Empty, endStream: true);
                requestFeature.Body = state.GetBodyStream();
            }

            var features = FeatureCollectionFactory.Create(_ops.PoolContext!, requestFeature, hasBody,
                _ops.ConnectionFeature, _ops.TlsHandshakeFeature, _maxRequestBodySize);
            features.Set<IHttpStreamIdFeature>(new GaudiStreamIdFeature(streamId));

            var capturedStreamId = streamId;
            features.Set<IHttpResetFeature>(new GaudiHttpResetFeature(errorCode =>
                EmitRstStream(capturedStreamId, (ErrorCode)errorCode)));
            features.Set(new GaudiInformationalResponseFeature((statusCode, headers) =>
                SendInformational(capturedStreamId, statusCode, headers)));

            _ops.OnRequest(features);

            if (string.Equals(requestFeature.Headers[WellKnownHeaders.Expect], "100-continue",
                    StringComparison.OrdinalIgnoreCase))
            {
                SendInformational(capturedStreamId, 100, new HeaderDictionary());
            }
        }
    }

    private void HandleDataFrame(DataFrame dataFrame, long streamId, StreamState state)
    {
        if (!state.HasBodyReader)
        {
            var queued = _ops.PoolContext!.Rent(() => new QueuedBodyReader(capacity: 8));
            state.InitBodyReader(queued, _maxRequestBodySize);
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
        _pump?.Cancel(streamId);

        if (_streams.TryGetValue(streamId, out var streamData))
        {
            var (decoder, state) = streamData;

            _ops.OnCancelTimer(state.BodyConsumptionTimerKey);
            _ops.OnCancelTimer(state.HeadersTimeoutTimerKey);
            ReturnDecoder(decoder);
            ReturnBodyReader(state);
            state.Reset();
            _poolContext.Return(state);

            _streams.Remove(streamId);
        }
    }

    private FrameDecoder RentDecoder() => _poolContext.Rent(static () => new FrameDecoder());

    private void ReturnDecoder(FrameDecoder decoder) => _poolContext.Return(decoder);


    IActorRef IMultiplexedBodyDrainTarget.StageActor => _ops.StageActor;

    void IMultiplexedBodyDrainTarget.EmitDataFrames(long streamId, ReadOnlyMemory<byte> data, bool endStream)
    {
        if (!data.IsEmpty)
        {
            EmitBufferedDataFrames(streamId, data, endStream);
        }
    }

    void IMultiplexedBodyDrainTarget.OnDrainComplete(long streamId)
    {
        Tracing.For("Protocol").Debug(this, "HTTP/3: response body complete (stream={0})", streamId);

        if (_streams.TryGetValue(streamId, out var streamData))
        {
            streamData.State.MarkBodyDrainComplete();
            EmitEndOfBody(streamId, streamData.State);
            CloseStream(streamId);
        }
    }

    void IMultiplexedBodyDrainTarget.OnDrainFailed(long streamId, Exception reason)
    {
        Tracing.For("Protocol").Warning(this,
            "HTTP/3: Response body drain failed for stream {0}: {1}", streamId, reason.Message);
        EmitRstStream(streamId, ErrorCode.GeneralProtocolError);
    }

    private void EmitEndOfBody(long streamId, StreamState state)
    {
        var features = state.GetFeatures();
        var trailerFeature = features?.Get<IHttpResponseTrailersFeature>();

        if (trailerFeature?.Trailers is { Count: > 0 } trailers)
        {
            if (trailers is GaudiHeaderDictionary gaudiTrailers)
            {
                gaudiTrailers.SetReadOnly();
            }

            var trailerFrame = _responseEncoder.EncodeTrailers(trailers);
            if (trailerFrame is not null)
            {
                EmitDataFrame(trailerFrame, streamId);
                Tracing.For("Protocol").Debug(this, "HTTP/3: response trailers emitted (stream={0})", streamId);
            }
        }

        _ops.OnOutbound(new CompleteWrites(streamId));
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
                break;
        }

        buf.Length = serialized;
        _ops.OnOutbound(MultiplexedData.Rent(buf, streamId));
    }

    private void EmitBufferedDataFrames(long streamId, ReadOnlyMemory<byte> body, bool endStream)
    {
        if (body.IsEmpty)
        {
            return;
        }

        var typeVarIntLen = QuicVarInt.EncodedLength((long)FrameType.Data);
        var payloadVarIntLen = QuicVarInt.EncodedLength(body.Length);
        var prefixSize = typeVarIntLen + payloadVarIntLen;
        var totalWireSize = prefixSize + body.Length;

        var buf = TransportBuffer.Rent(totalWireSize);
        var span = buf.FullMemory.Span;

        QuicVarInt.Encode((long)FrameType.Data, span);
        span = span[typeVarIntLen..];
        QuicVarInt.Encode(body.Length, span);
        span = span[payloadVarIntLen..];
        body.Span.CopyTo(span);

        buf.Length = totalWireSize;

        Tracing.For("Protocol").Trace(this, "HTTP/3: DATA out (stream={0}, len={1}, endStream={2})",
            streamId, body.Length, endStream);

        _responseRate.Observe(streamId, body.Length, Now());
        EnsureRateTimer();

        _ops.OnOutbound(MultiplexedData.Rent(buf, streamId));
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

        return MultiplexedData.Rent(buf, CriticalStreamId.Control);
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
            _ops.PoolContext!.Return(queued);
        }
        else
        {
            reader?.Dispose();
        }
    }
}