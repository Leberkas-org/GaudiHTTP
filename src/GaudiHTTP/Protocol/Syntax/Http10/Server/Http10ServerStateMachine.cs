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

internal sealed class Http10ServerStateMachine : IServerStateMachine, IBodyDrainTarget
{
    private readonly IServerStageOperations _ops;
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
        TimeProvider? timeProvider = null)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(options);
        _maxRequestBodySize = options.Limits.MaxRequestBodySize;
        _responseBodyChunkSize = options.ResponseBodyChunkSize;
        _rateGuard = new ConnectionRateGuard(ops, options.ToRateMonitor(), timeProvider);

        _decoder = new Http10ServerDecoder(options.ToHttp10DecoderOptions());
        _encoder = new Http10ServerEncoder(options.ToHttp10EncoderOptions());
    }

    public void PreStart()
    {
    }

    private CancellationTokenSource EnsureConnectionCts()
    {
        return _connectionCts ??= new CancellationTokenSource();
    }

    IActorRef IBodyDrainTarget.StageActor => _ops.StageActor;

    void IBodyDrainTarget.EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
    {
        if (!data.IsEmpty)
        {
            _rateGuard.ObserveResponse(0, data.Length);
            var item = WireBuffer.Rent(data.Length);
            data.CopyTo(item.FullMemory);
            item.Length = data.Length;
            _ops.OnOutbound(TransportData.Rent(item));
            Tracing.For("Protocol").Trace(this, "HTTP/1.0 response body chunk flushed (bytes={0})", data.Length);

            _serialPump!.OnCapacityAvailable();
        }

        EmitEndStreamIfNeeded(endStream);
    }

    void IBodyDrainTarget.EmitOwnedDataFrames(int streamId, IMemoryOwner<byte> owner, int bytesWritten, bool endStream)
    {
        if (bytesWritten > 0)
        {
            _rateGuard.ObserveResponse(0, bytesWritten);
            _ops.OnOutbound(TransportData.Rent(WireBuffer.Wrap(owner, 0, bytesWritten)));
            Tracing.For("Protocol").Trace(this, "HTTP/1.0 response body chunk flushed (bytes={0})", bytesWritten);
            _serialPump!.OnCapacityAvailable();
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
                _ops.OnResponseBodyComplete(_deferredFeatures);
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
            _ops.OnResponseBodyComplete(_deferredFeatures);
            _deferredFeatures = null;
        }

        Tracing.For("Protocol").Warning(this, "response body failed: {0}", reason.Message);
        ShouldComplete = true;
    }

    public void DecodeClientData(ITransportInbound data)
    {
        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        try
        {
            if (ShouldComplete)
            {
                return;
            }

            var pos = 0;

            if (_bodyStreaming && _decoder.StreamingReader is not null)
            {
                var outcome = _decoder.Feed(buffer.Memory[pos..], out _);
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

                return;
            }

            var result = _decoder.Feed(buffer.Memory[pos..], out var consumed);
            pos += consumed;

            if (_decoder.LastBodyBytesConsumed > 0)
            {
                _rateGuard.ObserveRequest(0, _decoder.LastBodyBytesConsumed);
            }

            if (result is DecodeOutcome.Complete or DecodeOutcome.HeadersReady)
            {
                var hasBody = result == DecodeOutcome.HeadersReady || _decoder.CurrentBodyReader is not null;
                var features = FeatureCollectionFactory.Create(hasBody,
                    out var feature, _ops.ConnectionFeature,
                    _ops.TlsHandshakeFeature, _maxRequestBodySize);
                _decoder.PopulateRequestFeature(feature);

                if (result != DecodeOutcome.HeadersReady)
                {
                    _rateGuard.RemoveRequest(0);
                }

                _ops.OnRequest(features);

                if (result == DecodeOutcome.HeadersReady)
                {
                    _bodyStreaming = true;

                    if (_decoder.StreamingReader is { } sr && _activeStreamingReader is null)
                    {
                        _activeStreamingReader = sr;
                        sr.SlotFreed += () =>
                            _ops.StageActor.Tell(new BodyResumed(), ActorRefs.NoSender);
                    }

                    if (pos < buffer.Memory.Length)
                    {
                        var bodyOutcome = _decoder.Feed(buffer.Memory[pos..], out _);
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
            Tracing.For("Protocol").Warning(this, "Failed to decode HTTP/1.0 request: {0}", ex.Message);
            ShouldComplete = true;
        }
        finally
        {
            buffer.Dispose();
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

                _serialPump = new SerialBodyPump(this, EnsureConnectionCts(), _responseBodyChunkSize, maxCapacity: 2);
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

    public void OnOutboundFlushed()
    {
        _serialPump?.OnCapacityAvailable();
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

    private void EncodeDeferredResponse(ReadOnlySpan<byte> body, bool suppressContentLength = false)
    {
        if (_deferredFeatures is null)
        {
            return;
        }

        WireBuffer? item = null;
        try
        {
            var bufferSize = 8 * 1024 + body.Length;
            item = WireBuffer.Rent(bufferSize);
            var written = _encoder.EncodeDeferred(item.FullMemory.Span, _deferredFeatures, body,
                suppressContentLength);
            item.Length = written;

            _ops.OnOutbound(TransportData.Rent(item));
        }
        catch (Exception ex)
        {
            item?.Dispose();

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
