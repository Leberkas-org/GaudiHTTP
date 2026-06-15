using System.Buffers;
using Akka.Actor;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Multiplexed;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;
using static Servus.Senf;

namespace TurboHTTP.Protocol.Syntax.Http3.Server;

internal readonly record struct StreamBodyReadComplete(long StreamId, int BytesRead);
internal readonly record struct StreamBodyReadFailed(long StreamId, Exception Reason);

internal sealed class Http3ServerSessionManager
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
    private readonly Dictionary<long, Stream> _activeBodyStreams = new();
    private readonly Dictionary<long, IMemoryOwner<byte>> _activeBodyBuffers = new();
    private readonly Dictionary<long, CancellationTokenSource> _activeBodyReadCts = new();
    private readonly HashSet<long> _drainReadInFlight = new();
    private readonly HashSet<long> _drainBufferOrphaned = new();
    private readonly StackStreamStatePool<StreamState> _statePool;
    private readonly Stack<FrameDecoder> _decoderPool = new();
    private const int MaxDecoderPoolSize = 256;
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

        if (state.HasBodyReader && _bodyConsumptionTimeout > TimeSpan.Zero)
        {
            _ops.OnScheduleTimer(state.BodyConsumptionTimerKey, _bodyConsumptionTimeout);
        }

        var headersFrame = _responseEncoder.EncodeHeaders(features);
        EmitDataFrame(headersFrame, streamId);

        var responseFeature = features.Get<IHttpResponseFeature>();
        var responseBody = features.Get<IHttpResponseBodyFeature>();
        var contentLength = ExtractContentLength(responseFeature);
        var hasStarted = responseBody is TurboHttpResponseBodyFeature { HasStarted: true };
        var hasBody = contentLength is not null and not 0
                      || (contentLength is null && hasStarted);

        if (!hasBody || responseBody is not TurboHttpResponseBodyFeature turboBody)
        {
            _ops.OnOutbound(new CompleteWrites(streamId));
            return;
        }

        if (turboBody.TryGetBufferedBody(out var bufferedBody))
        {
            if (bufferedBody.Length > 0)
            {
                EmitDataFrame(new DataFrame(bufferedBody), streamId);
            }

            _ops.OnOutbound(new CompleteWrites(streamId));
            CloseStream(streamId);
            return;
        }

        var bodyStream = turboBody.GetResponseStream();
        state.MarkBodyDrainActive();
        StartStreamBodyDrain(streamId, bodyStream, contentLength);
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
                    "HTTP/3: Response body drain failed for stream {0}: {1}", failed.StreamId,
                    failed.Reason.Message);
                EmitRstStream(failed.StreamId, ErrorCode.GeneralProtocolError);
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

        if (!_streams.TryGetValue(read.StreamId, out var streamData))
        {
            CleanupBodyDrain(read.StreamId);
            return;
        }

        var (_, state) = streamData;
        state.IsBodyReadPending = false;

        if (read.BytesRead == 0)
        {
            Tracing.For("Protocol").Debug(this, "HTTP/3: response body complete (stream={0})", read.StreamId);
            state.MarkBodyDrainComplete();

            if (!state.HasPendingOutbound)
            {
                _ops.OnOutbound(new CompleteWrites(read.StreamId));
                CleanupBodyDrain(read.StreamId);
                CloseStream(read.StreamId);
            }
            else
            {
                CleanupBodyDrain(read.StreamId);
            }

            return;
        }

        Tracing.For("Protocol").Trace(this, "HTTP/3: response body chunk (stream={0}, bytes={1})", read.StreamId, read.BytesRead);
        if (!_activeBodyBuffers.TryGetValue(read.StreamId, out var buffer))
        {
            CleanupBodyDrain(read.StreamId);
            return;
        }

        var data = buffer.Memory[..read.BytesRead];

        var dataFrame = new DataFrame(data);
        EmitDataFrame(dataFrame, read.StreamId);

        if (read.BytesRead > 0)
        {
            _responseRate.Observe(read.StreamId, read.BytesRead, Now());
            EnsureRateTimer();
        }

        ReadNextBodyChunk(read.StreamId);
    }

    public void DrainOutboundBuffer(long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var streamData))
        {
            return;
        }

        var (_, state) = streamData;

        while (state.PeekBodyChunk() is { } chunk)
        {
            var dataFrame = new DataFrame(chunk.Owner.Memory[..chunk.Length]);
            EmitDataFrame(dataFrame, streamId);

            state.TryDequeueBodyChunk(out _);
            chunk.Owner.Dispose();
        }

        if (state is { HasPendingOutbound: false, IsBodyDrainComplete: true })
        {
            _ops.OnOutbound(new CompleteWrites(streamId));
            CloseStream(streamId);
        }
        else if (!state.HasPendingOutbound && state.HasBodyDrain && !state.IsBodyDrainComplete && !state.IsBodyReadPending)
        {
            ReadNextBodyChunk(streamId);
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

        foreach (var (_, (decoder, state)) in _streams)
        {
            ReturnDecoder(decoder);
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
        _ops.OnOutbound(new MultiplexedData(buf, CriticalStreamId.QpackDecoder));
    }

    private void ProcessFrameData(TransportBuffer buffer, long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var streamData))
        {
            var frameDecoder = RentDecoder();
            var streamState = _statePool.Rent();
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

            var features = FeatureCollectionFactory.Create(_ops.PoolContext!, requestFeature, hasBody, _ops.Services,
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
        if (!state.HasBodyReader)
        {
            var queued = new QueuedBodyReader(capacity: 8);
            queued.Reset();
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
        CleanupBodyDrain(streamId);

        if (_streams.TryGetValue(streamId, out var streamData))
        {
            var (decoder, state) = streamData;

            _ops.OnCancelTimer(state.BodyConsumptionTimerKey);
            _ops.OnCancelTimer(state.HeadersTimeoutTimerKey);
            ReturnDecoder(decoder);
            state.Reset();
            _statePool.Return(state);

            _streams.Remove(streamId);
        }
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

    private void ReturnDecoder(FrameDecoder decoder)
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

    private void StartStreamBodyDrain(long streamId, Stream bodyStream, long? contentLength = null)
    {
        _activeBodyStreams[streamId] = bodyStream;
        var bufferSize = contentLength is > 0 and <= int.MaxValue
            ? (int)Math.Min(contentLength.Value, _responseBodyChunkSize)
            : _responseBodyChunkSize;
        var buffer = MemoryPool<byte>.Shared.Rent(Math.Max(bufferSize, 256));
        _activeBodyBuffers[streamId] = buffer;
        _activeBodyReadCts[streamId] = new CancellationTokenSource();
        ReadNextBodyChunk(streamId);
    }

    private void ReadNextBodyChunk(long streamId)
    {
        if (!_activeBodyStreams.TryGetValue(streamId, out var stream) ||
            !_activeBodyBuffers.TryGetValue(streamId, out var buffer))
        {
            return;
        }

        if (_streams.TryGetValue(streamId, out var streamData))
        {
            streamData.State.IsBodyReadPending = true;
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

    private void CleanupBodyDrain(long streamId)
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

    private void DisposeDrainResources(long streamId)
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
