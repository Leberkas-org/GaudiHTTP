using System.Buffers;
using Akka.Actor;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Streams.Stages.Server;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Syntax.Http10.Server;

internal sealed class Http10ServerStateMachine :
    TcpStateMachineBase<IServerStageOperations>, IServerStateMachine, IBodyDrainTarget
{
    private readonly Http10ServerDecoder _decoder;
    private readonly Http10ServerEncoder _encoder;
    private readonly long _maxRequestBodySize;
    private readonly int _responseBodyChunkSize;
    private readonly ConnectionRateGuard _rateGuard;

    private IFeatureCollection? _deferredFeatures;
    private bool _bodyStreaming;
    private IStreamingBodyReader? _activeStreamingReader;
    private SerialBodyPump? _serialPump;
    private CancellationTokenSource? _connectionCts;
    private bool _closeAfterBody;

    public bool CanAcceptResponse => true;
    public bool ShouldComplete { get; private set; }
    public bool ShouldPauseNetwork => _activeStreamingReader?.IsFull ?? false;

    public int MaxQueuedRequests => 1;

    // HTTP/1.0 dispatches one request per connection; mirror H1.1 so handler dispatch stays serial.
    public int MaxConcurrentRequests => 1;

    public Http10ServerStateMachine(Http1ConnectionOptions options, IServerStageOperations ops,
        TimeProvider? timeProvider = null) : base(ops)
    {
        ArgumentNullException.ThrowIfNull(ops);
        ArgumentNullException.ThrowIfNull(options);
        _maxRequestBodySize = options.Limits.MaxRequestBodySize;
        _responseBodyChunkSize = options.ResponseBodyChunkSize;
        _rateGuard = new ConnectionRateGuard(ops, options.ToRateMonitor(), timeProvider);

        _decoder = new Http10ServerDecoder(options.ToHttp10DecoderOptions());
        _encoder = new Http10ServerEncoder(options.ToHttp10EncoderOptions());
    }

    protected override IActorRef Self => Ops.Self;
    protected override bool ShouldPauseReads => ShouldPauseNetwork;
    protected override void OnFlushCompleted() => _serialPump?.OnCapacityAvailable(int.MaxValue);
    protected override void OnFlushDeferred() => _serialPump?.ParkForFlush();
    protected override void OnTransportLost(Exception? ex) => ShouldComplete = true;
    protected override void OnTransportConnected(Servus.Akka.Transport.ConnectionInfo info)
    {
        _serialPump?.OnCapacityAvailable(int.MaxValue);
    }
    protected override void OnTransportDisconnected(DisconnectReason reason) => ShouldComplete = true;

    public void PreStart()
    {
    }

    private CancellationTokenSource EnsureConnectionCts()
    {
        return _connectionCts ??= new CancellationTokenSource();
    }

    IActorRef IBodyDrainTarget.StageActor => Ops.StageActor;

    void IBodyDrainTarget.EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
    {
        if (!data.IsEmpty)
        {
            _rateGuard.ObserveResponse(0, data.Length);
            var transport = Transport!;
            var mem = transport.GetMemory(data.Length);
            data.CopyTo(mem);
            transport.Advance(data.Length);
            RequestFlush();

            Tracing.For("Protocol").Trace(this, "HTTP/1.0 response body chunk flushed (bytes={0})", data.Length);
            _serialPump?.ResetCredit();
        }

        EmitEndStreamIfNeeded(endStream);
    }

    void IBodyDrainTarget.EmitOwnedDataFrames(int streamId, IMemoryOwner<byte> owner, int bytesWritten, bool endStream)
    {
        if (bytesWritten > 0)
        {
            _rateGuard.ObserveResponse(0, bytesWritten);
            var transport = Transport!;
            var mem = transport.GetMemory(bytesWritten);
            owner.Memory.Span[..bytesWritten].CopyTo(mem.Span);
            transport.Advance(bytesWritten);
            owner.Dispose();
            RequestFlush();

            Tracing.For("Protocol").Trace(this, "HTTP/1.0 response body chunk flushed (bytes={0})", bytesWritten);
            _serialPump?.ResetCredit();
        }
        else
        {
            owner.Dispose();
        }

        EmitEndStreamIfNeeded(endStream);
    }

    private void EmitEndStreamIfNeeded(bool endStream)
    {
        if (endStream)
        {
            _rateGuard.RemoveResponse(0);
            if (_deferredFeatures is not null)
            {
                Ops.OnResponseBodyComplete(_deferredFeatures);
                _deferredFeatures = null;
            }

            if (_closeAfterBody)
            {
                ShouldComplete = true;
            }

            Tracing.For("Protocol").Debug(this, "HTTP/1.0 response body complete (pump)");
        }
    }

    void IBodyDrainTarget.OnDrainComplete(int streamId)
    {
        Tracing.For("Protocol").Debug(this, "HTTP/1.0 response body drain complete");
    }

    void IBodyDrainTarget.OnDrainFailed(int streamId, Exception reason)
    {
        _rateGuard.RemoveResponse(0);
        if (_deferredFeatures is not null)
        {
            Ops.OnResponseBodyComplete(_deferredFeatures);
            _deferredFeatures = null;
        }

        Tracing.For("Protocol").Warning(this, "response body failed: {0}", reason.Message);
        ShouldComplete = true;
    }

    public void DecodeClientData(ITransportInbound data)
    {
        if (DispatchLifecycleEvent(data))
        {
            return;
        }

        if (data is TransportData)
        {
            throw new InvalidOperationException("TransportData is not supported on TCP state machines; use pipe transport.");
        }
    }

    public void OnResponse(IFeatureCollection features)
    {
        _deferredFeatures = features;

        var responseBody = features.Get<IHttpResponseBodyFeature>();
        if (responseBody is GaudiHttpResponseBodyFeature gaudiBody)
        {
            if (gaudiBody.TryGetBufferedBody(out var bufferedBody))
            {
                EncodeDeferredResponse(bufferedBody.Span);
                return;
            }

            var bodyStream = gaudiBody.GetResponseStream();
            if (bodyStream is not null)
            {
                var contentLength = ExtractContentLength(features.Get<IHttpResponseFeature>());

                // HTTP/1.0 without Content-Length: the client reads until connection close.
                // Defer ShouldComplete until the body is fully emitted so the stage doesn't
                // close the connection while the pump still has async reads pending.
                if (!contentLength.HasValue)
                {
                    _closeAfterBody = true;
                }

                _serialPump = new SerialBodyPump(this, EnsureConnectionCts(), _responseBodyChunkSize, maxBytes: 256 * 1024);
                EncodeDeferredResponse(ReadOnlySpan<byte>.Empty, suppressContentLength: _closeAfterBody);
                _serialPump.Register(bodyStream, contentLength: null, CancellationToken.None);
                return;
            }
        }

        EncodeDeferredResponse(ReadOnlySpan<byte>.Empty);
    }

    public void OnDownstreamFinished()
    {
    }

    public void OnTimerFired(string name)
    {
        if (name == ConnectionRateGuard.TimerName)
        {
            if (_rateGuard.OnTimerFired((req, resp) =>
                    Tracing.For("Protocol").Warning(this,
                        "data rate violation (reqRate={0}, respRate={1})", req, resp)))
            {
                ShouldComplete = true;
            }
        }
    }

    public void OnBodyMessage(object msg)
    {
        if (TryHandleAsyncResult(msg))
        {
            return;
        }

        switch (msg)
        {
            case BodyReadComplete<int> read:
                _serialPump?.HandleReadComplete(read.BytesRead);
                break;

            case BodyReadFailed<int> failed:
                _serialPump?.HandleReadFailed(failed.Reason);
                break;
        }
    }

    protected override (SequencePosition Consumed, SequencePosition Examined) DecodeData(ReadOnlySequence<byte> data)
    {
        ReadOnlyMemory<byte> memory;
        byte[]? rented = null;
        if (data.IsSingleSegment)
        {
            memory = data.First;
        }
        else
        {
            rented = ArrayPool<byte>.Shared.Rent((int)data.Length);
            data.CopyTo(rented);
            memory = rented.AsMemory(0, (int)data.Length);
        }

        var pos = 0;
        try
        {
            if (ShouldComplete)
            {
                return (data.End, data.End);
            }

            if (_bodyStreaming && _decoder.StreamingReader is not null)
            {
                var outcome = _decoder.Feed(memory[pos..], out _);
                if (_decoder.LastBodyBytesConsumed > 0)
                {
                    _rateGuard.ObserveRequest(0, _decoder.LastBodyBytesConsumed);
                }

                if (outcome == DecodeOutcome.Complete)
                {
                    _bodyStreaming = false;
                    _activeStreamingReader = null;
                    _rateGuard.RemoveRequest(0);
                }

                return (data.End, data.End);
            }

            var result = _decoder.Feed(memory[pos..], out var consumed);
            pos += consumed;

            if (_decoder.LastBodyBytesConsumed > 0)
            {
                _rateGuard.ObserveRequest(0, _decoder.LastBodyBytesConsumed);
            }

            if (result is DecodeOutcome.Complete or DecodeOutcome.HeadersReady)
            {
                var hasBody = result == DecodeOutcome.HeadersReady || _decoder.CurrentBodyReader is not null;
                var features = FeatureCollectionFactory.Create(hasBody,
                    out var feature, Ops.ConnectionFeature,
                    Ops.TlsHandshakeFeature, _maxRequestBodySize);
                _decoder.PopulateRequestFeature(feature);

                if (result != DecodeOutcome.HeadersReady)
                {
                    _rateGuard.RemoveRequest(0);
                }

                Ops.OnRequest(features);

                if (result == DecodeOutcome.HeadersReady)
                {
                    _bodyStreaming = true;

                    if (_decoder.StreamingReader is { } sr && _activeStreamingReader is null)
                    {
                        _activeStreamingReader = sr;
                        sr.SlotFreed += () =>
                            Ops.StageActor.Tell(new BodyResumed(), ActorRefs.NoSender);
                    }

                    if (pos < memory.Length)
                    {
                        var bodyOutcome = _decoder.Feed(memory[pos..], out _);
                        if (_decoder.LastBodyBytesConsumed > 0)
                        {
                            _rateGuard.ObserveRequest(0, _decoder.LastBodyBytesConsumed);
                        }

                        if (bodyOutcome == DecodeOutcome.Complete)
                        {
                            _bodyStreaming = false;
                            _activeStreamingReader = null;
                            _rateGuard.RemoveRequest(0);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Tracing.For("Protocol").Warning(this, "Failed to decode HTTP/1.0 request (pipe): {0}", ex.Message);
            ShouldComplete = true;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        return (data.End, data.End);
    }

    private void EncodeDeferredResponse(ReadOnlySpan<byte> body, bool suppressContentLength = false)
    {
        if (_deferredFeatures is null)
        {
            return;
        }

        try
        {
            var transport = Transport!;
            var writer = new TransportBufferWriter(transport);
            _encoder.EncodeDeferred(writer, _deferredFeatures, body, suppressContentLength);
            RequestFlush();
        }
        catch (Exception ex)
        {
            Tracing.For("Protocol").Error(this, "Failed to encode HTTP/1.0 response: {0}", ex.Message);
        }
        finally
        {
            // Only clear _deferredFeatures when NOT using the pump path.
            // The pump path keeps _deferredFeatures alive so OnResponseBodyComplete
            // can be called after body streaming finishes.
            if (_serialPump is null)
            {
                _deferredFeatures = null;
            }
        }
    }

    public void ResumeBody()
    {
        RequestRead();
    }

    public void Cleanup()
    {
        _activeStreamingReader = null;
        _deferredFeatures = null;
        _serialPump?.Cleanup();
        _serialPump = null;
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _connectionCts = null;
        CleanupTransportIo();
        _rateGuard.Cleanup();
    }

    private static long? ExtractContentLength(IHttpResponseFeature? responseFeature)
    {
        if (responseFeature?.Headers is null)
        {
            return null;
        }

        foreach (var header in responseFeature.Headers)
        {
            if (header.Key.Equals(WellKnownHeaders.ContentLength, StringComparison.OrdinalIgnoreCase)
                && header.Value.Count > 0
                && header.Value[0] is { } value
                && ContentLengthSemantics.TryParse(value, out var length))
            {
                return length;
            }
        }

        return null;
    }
}
