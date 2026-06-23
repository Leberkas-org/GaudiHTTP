using System.Buffers;
using Akka.Actor;
using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Pooling;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Multiplexed;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Streams.Stages.Client;
using static Servus.Senf;

namespace TurboHTTP.Protocol.Syntax.Http3.Client;

internal sealed class Http3ClientSessionManager : IBodyDrainTarget<long>
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

    private readonly Dictionary<long, HttpContent> _drainContentOwners = new();
    private readonly CancellationTokenSource _connectionCts = new();
    private readonly ConnectionPoolContext _poolContext = new();
    private MultiplexedBodyPump? _pump;

    private bool _controlPrefaceSent;
    private bool _transportConnected;
    private readonly List<ITransportOutbound> _preConnectBuffer = [];

    public bool CanOpenStream => _tracker.CanOpenStream();
    public bool HasInFlightRequests => _streamManager.HasInFlightRequests;
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
    }

    public void EncodeRequest(HttpRequestMessage request)
    {
        if (!_tracker.CanOpenStream())
        {
            Tracing.For("Protocol").Warning(this,
                "HTTP/3: EncodeRequest called at MaxConcurrentStreams limit (active={0}, max={1})",
                _tracker.ActiveStreamCount, _tracker.MaxConcurrentStreams);
        }

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
        _drainContentOwners[streamId] = request.Content!;
        _pump ??= new MultiplexedBodyPump(this, _poolContext, _connectionCts, _options.RequestBodyChunkSize);
        _pump.Register(streamId, bodyStream!, contentLength, CancellationToken.None);
    }

    public void OnBodyMessage(object msg)
    {
        switch (msg)
        {
            case DrainReadComplete<long> read:
                _pump?.HandleReadComplete(read.StreamId, read.BytesRead);
                break;

            case DrainReadFailed<long> failed:
                _pump?.HandleReadFailed(failed.StreamId, failed.Reason);
                break;

            case DrainContinue<long> cont:
                _pump?.HandleDrainContinue(cont.StreamId);
                break;
        }
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

        return MultiplexedData.Rent(buf, CriticalStreamId.Control);
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
        return _streamManager.GetCorrelationMap();
    }

    public List<HttpRequestMessage> SnapshotAndClearCorrelations()
    {
        return _streamManager.SnapshotAndClearCorrelations();
    }

    public bool TryCancelStream(HttpRequestMessage request)
    {
        if (!_streamManager.TryFindStreamByRequest(request, out var streamId))
        {
            return false;
        }

        EmitOutbound(new ResetStream(streamId, 0x10C));
        _streamManager.RemoveCorrelation(streamId);
        request.Fail(new OperationCanceledException("Request cancelled by caller."));
        _pump?.Cancel(streamId);
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
        _pump?.Cleanup();
        _drainContentOwners.Clear();

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

    IActorRef IBodyDrainTarget<long>.StageActor => _ops.StageActor;

    void IBodyDrainTarget<long>.EmitDataFrames(long streamId, ReadOnlyMemory<byte> data, bool endStream)
    {
        EmitBufferedDataFrames(streamId, data, endStream);
    }

    private void EmitBufferedDataFrames(long streamId, ReadOnlyMemory<byte> body, bool endStream)
    {
        if (body.IsEmpty)
        {
            if (endStream)
            {
                EmitOutbound(new CompleteWrites(StreamTarget.FromId(streamId)));
            }
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
        EmitOutbound(MultiplexedData.Rent(buf, streamId));

        if (endStream)
        {
            EmitOutbound(new CompleteWrites(StreamTarget.FromId(streamId)));
        }
    }

    void IBodyDrainTarget<long>.OnDrainComplete(long streamId)
    {
        _drainContentOwners.Remove(streamId);

        var state = _streamManager.TryGetStreamState(streamId);
        if (state is not null)
        {
            Tracing.For("Protocol").Debug(this, "HTTP/3: request body complete (stream={0})", streamId);
            state.MarkBodyDrainComplete();
        }
    }

    void IBodyDrainTarget<long>.OnDrainFailed(long streamId, Exception reason)
    {
        _drainContentOwners.Remove(streamId);
        Tracing.For("Protocol").Warning(this,
            "HTTP/3: Body drain failed for stream {0}: {1}", streamId, reason.Message);
        EmitOutbound(new ResetStream(streamId));
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
        EmitOutbound(MultiplexedData.Rent(buf, streamId));
    }

    private void EmitSerializedFrame(Http3Frame frame, long streamId)
    {
        var buf = TransportBuffer.Rent(frame.SerializedSize);
        var span = buf.FullMemory.Span;
        frame.WriteTo(ref span);
        buf.Length = frame.SerializedSize;

        EmitOutbound(MultiplexedData.Rent(buf, streamId));
    }
}
