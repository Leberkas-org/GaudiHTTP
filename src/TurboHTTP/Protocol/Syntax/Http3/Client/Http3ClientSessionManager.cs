using System.Buffers;
using Akka.Actor;
using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Multiplexed;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Streams.Stages.Client;
using static Servus.Senf;

namespace TurboHTTP.Protocol.Syntax.Http3.Client;

internal readonly record struct StreamBodyReadComplete(long StreamId, int BytesRead);
internal readonly record struct StreamBodyReadFailed(long StreamId, Exception Reason);

internal sealed class Http3ClientSessionManager
{
    private readonly Http3ClientEncoderOptions _encoderOptions;
    private readonly Http3ClientDecoderOptions _decoderOptions;
    private readonly TurboClientOptions _options;
    private readonly IClientStageOperations _ops;

    private readonly QuicStreamTracker _tracker;
    private readonly QpackStreamManager _qpackStreamManager;
    private readonly StreamManager _streamManager;

    private readonly Http3ClientEncoder _requestEncoder;
    private readonly QpackTableSync _tableSync;

    private readonly Dictionary<long, HttpRequestMessage> _correlationMap = new();
    private readonly Dictionary<long, Stream> _activeBodyStreams = new();
    private readonly Dictionary<long, IMemoryOwner<byte>> _activeBodyBuffers = new();

    private bool _controlPrefaceSent;
    private bool _transportConnected;
    private readonly List<ITransportOutbound> _preConnectBuffer = [];

    public bool CanOpenStream => _tracker.CanOpenStream();
    public bool HasInFlightRequests => _correlationMap.Count > 0 || _streamManager.HasInFlightRequests;
    public RequestEndpoint Endpoint { get; private set; }

    public Http3ClientSessionManager(
        Http3ClientEncoderOptions encoderOptions,
        Http3ClientDecoderOptions decoderOptions,
        TurboClientOptions options,
        IClientStageOperations ops)
    {
        _encoderOptions = encoderOptions;
        _decoderOptions = decoderOptions;
        _options = options;
        _ops = ops;

        _tracker = new QuicStreamTracker(initialNextStreamId: 0, decoderOptions.MaxConcurrentStreams);

        _tableSync = new QpackTableSync(
            encoderMaxCapacity: 0,
            decoderMaxCapacity: encoderOptions.QpackMaxTableCapacity,
            maxBlockedStreams: encoderOptions.QpackBlockedStreams,
            configuredEncoderLimit: encoderOptions.QpackMaxTableCapacity);

        _requestEncoder = new Http3ClientEncoder(_tableSync);
        var responseDecoder = new Http3ClientDecoder(_tableSync, decoderOptions.MaxFieldSectionSize);
        _qpackStreamManager = new QpackStreamManager(ops, _requestEncoder, responseDecoder, _tableSync);
        _streamManager = new StreamManager(ops, responseDecoder, _tableSync, _options.MaxStreamedResponseBodySize ?? long.MaxValue)
        {
            OnStreamClosedCallback = OnStreamClosed
        };
    }

    private void OnStreamClosed(long streamId)
    {
        _tracker.OnStreamClosed(streamId);
        _correlationMap.Remove(streamId);
    }

    public void EncodeRequest(HttpRequestMessage request)
    {
        var endpoint = request.RequestUri is not null
            ? RequestEndpoint.FromRequest(request)
            : RequestEndpoint.Default;

        if (Endpoint == default && endpoint != default)
        {
            Endpoint = endpoint;
            var transportOptions = OptionsFactory.Build(Endpoint, _options);
            _ops.OnOutbound(new ConnectTransport(transportOptions));

            var preface = TryBuildControlPreface();
            if (preface is not null)
            {
                _ops.OnOutbound(preface);
            }
        }

        var streamId = _tracker.AllocateStreamId();
        _tracker.OnStreamOpened(streamId);

        EmitOutbound(new OpenStream(StreamTarget.FromId(streamId), StreamDirection.Bidirectional));

        _correlationMap.TryAdd(streamId, request);
        _streamManager.Correlate(streamId, request);

        if (request.RequestUri is null)
        {
            return;
        }

        var frames = _requestEncoder.Encode(request);
        if (frames.Count == 0)
        {
            return;
        }

        _qpackStreamManager.FlushEncoderInstructions();

        EmitBatchedFrames(frames, streamId);

        if (request.Content is null)
        {
            EmitOutbound(new CompleteWrites(StreamTarget.FromId(streamId)));
            return;
        }

        var contentLength = request.Content?.Headers.ContentLength;
        var bodyStream = request.Content?.ReadAsStream();

        if (bodyStream is MemoryStream ms && ms.TryGetBuffer(out var segment))
        {
            var pos = (int)ms.Position;
            var available = segment.Count - pos;
            if (available > 0)
            {
                var dataFrame = new DataFrame(segment.AsMemory(pos, available));
                EmitSerializedFrame(dataFrame, streamId);
                EmitOutbound(new CompleteWrites(StreamTarget.FromId(streamId)));
                return;
            }
        }

        if (contentLength is > 0 and { } knownLength
            && knownLength <= _options.Http3.MaxBufferedRequestBodySize
            && TrySerializeBodyDirect(request.Content!, streamId, (int)knownLength))
        {
            return;
        }

        var state = _streamManager.GetOrCreateStreamState(streamId);
        state.MarkBodyDrainActive();
        StartStreamBodyDrain(streamId, bodyStream!, contentLength);
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
                    "HTTP/3: Body drain failed for stream {0}: {1}", failed.StreamId, failed.Reason.Message);
                EmitOutbound(new ResetStream(failed.StreamId));
                CleanupBodyDrain(failed.StreamId);
                break;
        }
    }

    private void HandleStreamBodyRead(StreamBodyReadComplete read)
    {
        var state = _streamManager.TryGetStreamState(read.StreamId);
        if (state is null)
        {
            CleanupBodyDrain(read.StreamId);
            return;
        }

        if (read.BytesRead == 0)
        {
            Tracing.For("Protocol").Debug(this, "HTTP/3: request body complete (stream={0})", read.StreamId);
            EmitOutbound(new CompleteWrites(StreamTarget.FromId(read.StreamId)));
            state.MarkBodyDrainComplete();
            CleanupBodyDrain(read.StreamId);
            return;
        }

        Tracing.For("Protocol").Trace(this, "HTTP/3: request body chunk (stream={0}, bytes={1})", read.StreamId, read.BytesRead);
        if (!_activeBodyBuffers.TryGetValue(read.StreamId, out var buffer))
        {
            CleanupBodyDrain(read.StreamId);
            return;
        }

        var data = buffer.Memory[..read.BytesRead];

        var dataFrame = new DataFrame(data);
        EmitSerializedFrame(dataFrame, read.StreamId);
        ReadNextBodyChunk(read.StreamId);
    }

    public void OpenCriticalStreams()
    {
        QpackStreamManager.OpenCriticalStreams(EmitOutbound);
    }

    public MultiplexedData? TryBuildControlPreface()
    {
        if (_controlPrefaceSent)
        {
            return null;
        }

        _controlPrefaceSent = true;

        var settings = new Settings();
        settings.Set(SettingsIdentifier.QpackMaxTableCapacity, _encoderOptions.QpackMaxTableCapacity);
        settings.Set(SettingsIdentifier.QpackBlockedStreams, _encoderOptions.QpackBlockedStreams);
        settings.Set(SettingsIdentifier.MaxFieldSectionSize, _decoderOptions.MaxFieldSectionSize);
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

    public IReadOnlyList<Http3Frame> DecodeServerData(TransportBuffer buffer, long streamId)
    {
        return _streamManager.DecodeServerData(buffer, streamId);
    }

    public void AssembleResponse(Http3Frame frame, long streamId)
    {
        _streamManager.AssembleResponse(frame, streamId);
    }

    public void FlushPendingResponse(long streamId)
    {
        _streamManager.FlushPendingResponse(streamId);
    }

    public void FlushAllPendingResponses()
    {
        _streamManager.FlushAllPendingResponses();
    }

    public void ProcessQpackDecoderBytes(ReadOnlyMemory<byte> data)
    {
        _qpackStreamManager.ProcessDecoderInstructions(data.Span);
    }

    public void ProcessQpackEncoderBytes(ReadOnlyMemory<byte> data)
    {
        var resolved = _qpackStreamManager.ProcessEncoderInstructionsAndResolveBlocked(data.Span);
        _qpackStreamManager.FlushDecoderInstructions();
        _streamManager.ResolveBlockedStreams(resolved);
    }

    public void HandleSettings(SettingsFrame settings)
    {
        var remoteSettings = new Settings();
        foreach (var (id, val) in settings.Parameters)
        {
            remoteSettings.Set(id, val);
        }

        _qpackStreamManager.ApplyPeerSettings(remoteSettings);
    }

    public void OnTransportConnected()
    {
        _transportConnected = true;
        FlushPreConnectBuffer();
    }

    public void OnTransportDisconnected()
    {
        _transportConnected = false;
    }

    public IReadOnlyDictionary<long, HttpRequestMessage> GetCorrelationMap()
    {
        return _correlationMap;
    }

    public List<HttpRequestMessage> SnapshotAndClearCorrelations()
    {
        var snapshot = _correlationMap.Values.ToList();
        _correlationMap.Clear();
        return snapshot;
    }

    public bool TryCancelStream(HttpRequestMessage request)
    {
        long streamId = -1;
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

        EmitOutbound(new ResetStream(streamId, 0x10C));
        _correlationMap.Remove(streamId);
        request.Fail(new OperationCanceledException("Request cancelled by caller."));
        CleanupBodyDrain(streamId);
        _tracker.OnStreamClosed(streamId);

        return true;
    }

    public void ResetConnectionState()
    {
        _tracker.Reset();
        _controlPrefaceSent = false;
        _tableSync.Reset();
        _qpackStreamManager.Reset();
        _streamManager.ResetAllDecoders();
    }

    public void Cleanup()
    {
        foreach (var streamId in _activeBodyStreams.Keys.ToList())
        {
            CleanupBodyDrain(streamId);
        }

        _streamManager.Dispose();

        foreach (var item in _preConnectBuffer)
        {
            if (item is TransportData { Buffer: var buffer })
            {
                buffer.Dispose();
            }
        }

        _preConnectBuffer.Clear();
    }

    public void DrainStreams()
    {
        _streamManager.DrainStreams();
    }

    private void EmitOutbound(ITransportOutbound item)
    {
        if (item is ConnectTransport || _transportConnected)
        {
            _ops.OnOutbound(item);
            return;
        }

        _preConnectBuffer.Add(item);
    }

    private void FlushPreConnectBuffer()
    {
        for (var i = 0; i < _preConnectBuffer.Count; i++)
        {
            _ops.OnOutbound(_preConnectBuffer[i]);
        }

        _preConnectBuffer.Clear();
    }

    private bool TrySerializeBodyDirect(HttpContent content, long streamId, int bodyLength)
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
            pool.Return(bodyArray);
            return false;
        }

        var dataFrame = new DataFrame(new ReadOnlyMemory<byte>(bodyArray, 0, bodyLength));
        EmitSerializedFrame(dataFrame, streamId);
        pool.Return(bodyArray);
        EmitOutbound(new CompleteWrites(StreamTarget.FromId(streamId)));
        return true;
    }

    private void StartStreamBodyDrain(long streamId, Stream bodyStream, long? contentLength = null)
    {
        _activeBodyStreams[streamId] = bodyStream;
        var bufferSize = contentLength is > 0 and <= int.MaxValue
            ? (int)Math.Min(contentLength.Value, _options.RequestBodyChunkSize)
            : _options.RequestBodyChunkSize;
        var buffer = MemoryPool<byte>.Shared.Rent(Math.Max(bufferSize, 256));
        _activeBodyBuffers[streamId] = buffer;
        ReadNextBodyChunk(streamId);
    }

    private void ReadNextBodyChunk(long streamId)
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

    private void CleanupBodyDrain(long streamId)
    {
        if (_activeBodyBuffers.Remove(streamId, out var buffer))
        {
            buffer.Dispose();
        }

        _activeBodyStreams.Remove(streamId);
    }

    private void EmitBatchedFrames(IReadOnlyList<Http3Frame> frames, long streamId)
    {
        if (frames.Count == 0)
        {
            return;
        }

        if (frames.Count == 1)
        {
            EmitSerializedFrame(frames[0], streamId);
            return;
        }

        var totalSize = 0;
        for (var i = 0; i < frames.Count; i++)
        {
            totalSize += frames[i].SerializedSize;
        }

        var buf = TransportBuffer.Rent(totalSize);
        var span = buf.FullMemory.Span;
        var offset = 0;

        for (var i = 0; i < frames.Count; i++)
        {
            var frameSpan = span[offset..];
            var written = frames[i].WriteTo(ref frameSpan);
            offset += written;
        }

        buf.Length = offset;
        EmitOutbound(new MultiplexedData(buf, streamId));
    }

    private void EmitSerializedFrame(Http3Frame frame, long streamId)
    {
        var buf = TransportBuffer.Rent(frame.SerializedSize);
        var span = buf.FullMemory.Span;
        frame.WriteTo(ref span);
        buf.Length = frame.SerializedSize;

        EmitOutbound(new MultiplexedData(buf, streamId));
    }
}
