using Akka.Actor;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Pooling;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;
using static Servus.Senf;

namespace TurboHTTP.Protocol.Syntax.Http10.Server;

internal sealed class Http10ServerStateMachine : IServerStateMachine, IBodyDrainTarget<int>
{
    private const string DataRateCheck = "data-rate-check";

    private readonly IServerStageOperations _ops;
    private readonly Http10ServerDecoder _decoder;
    private readonly Http10ServerEncoder _encoder;
    private readonly long _maxRequestBodySize;
    private readonly DataRateMonitor _requestRate;
    private readonly DataRateMonitor _responseRate;
    private readonly List<long> _rateViolations = [];
    private bool _rateTimerActive;
    private readonly TimeProvider _clock;

    private long Now() => _clock.GetUtcNow().ToUnixTimeMilliseconds();

    private readonly ConnectionPoolContext _poolContext = new();
    private IFeatureCollection? _deferredFeatures;
    private bool _bodyStreaming;
    private IStreamingBodyReader? _activeStreamingReader;
    private SerialBodyPump? _serialPump;
    private CancellationTokenSource? _connectionCts;

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
        _clock = timeProvider ?? TimeProvider.System;

        var rate = options.ToRateMonitor();
        _requestRate = new DataRateMonitor(rate.MinRequestBodyDataRate, rate.MinRequestBodyDataRateGracePeriod);
        _responseRate = new DataRateMonitor(rate.MinResponseDataRate, rate.MinResponseDataRateGracePeriod);

        _decoder = new Http10ServerDecoder(options.ToHttp10DecoderOptions(), _poolContext);
        _encoder = new Http10ServerEncoder(options.ToHttp10EncoderOptions());
    }

    public void PreStart()
    {
    }

    private CancellationTokenSource EnsureConnectionCts()
    {
        return _connectionCts ??= new CancellationTokenSource();
    }

    IActorRef IBodyDrainTarget<int>.StageActor => _ops.StageActor;

    void IBodyDrainTarget<int>.EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
    {
        if (!data.IsEmpty)
        {
            _responseRate.Observe(0, data.Length, Now());
            EnsureRateTimer();
            var item = TransportBuffer.Rent(data.Length);
            data.CopyTo(item.FullMemory);
            item.Length = data.Length;
            _ops.OnOutbound(TransportData.Rent(item));
            Tracing.For("Protocol").Trace(this, "HTTP/1.0 response body chunk flushed (bytes={0})", data.Length);

            // H1.0 has no OnOutboundFlushed — drive the pump inline.
            _serialPump!.ResetSyncReadCounter();
            _serialPump.OnCapacityAvailable();
        }

        if (endStream)
        {
            _responseRate.Remove(0);
            if (_deferredFeatures is not null)
            {
                _ops.OnResponseBodyComplete(_deferredFeatures);
                _deferredFeatures = null;
            }

            Tracing.For("Protocol").Debug(this, "HTTP/1.0 response body complete (pump)");
        }
    }

    void IBodyDrainTarget<int>.OnDrainComplete(int streamId)
    {
        Tracing.For("Protocol").Debug(this, "HTTP/1.0 response body drain complete");
    }

    void IBodyDrainTarget<int>.OnDrainFailed(int streamId, Exception reason)
    {
        _responseRate.Remove(0);
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
                    _requestRate.Observe(0, _decoder.LastBodyBytesConsumed, Now());
                    EnsureRateTimer();
                }

                if (outcome == DecodeOutcome.Complete)
                {
                    _bodyStreaming = false;
                    _activeStreamingReader = null;
                    _requestRate.Remove(0);
                }

                return;
            }

            var result = _decoder.Feed(buffer.Memory[pos..], out var consumed);
            pos += consumed;

            if (_decoder.LastBodyBytesConsumed > 0)
            {
                _requestRate.Observe(0, _decoder.LastBodyBytesConsumed, Now());
                EnsureRateTimer();
            }

            if (result is DecodeOutcome.Complete or DecodeOutcome.HeadersReady)
            {
                var hasBody = result == DecodeOutcome.HeadersReady || _decoder.CurrentBodyReader is not null;
                var features = FeatureCollectionFactory.Create(_ops.PoolContext!, hasBody,
                    out var feature, _ops.ConnectionFeature,
                    _ops.TlsHandshakeFeature, _maxRequestBodySize);
                _decoder.PopulateRequestFeature(feature);

                if (result != DecodeOutcome.HeadersReady)
                {
                    _requestRate.Remove(0);
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
                            _requestRate.Observe(0, _decoder.LastBodyBytesConsumed, Now());
                            EnsureRateTimer();
                        }

                        if (bodyOutcome == DecodeOutcome.Complete)
                        {
                            _bodyStreaming = false;
                            _activeStreamingReader = null;
                            _requestRate.Remove(0);
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
        if (responseBody is TurboHttpResponseBodyFeature turboBody)
        {
            if (turboBody.TryGetBufferedBody(out var bufferedBody))
            {
                if (bufferedBody.Length > 0)
                {
                    EncodeDeferredResponse(bufferedBody.Span);
                }

                return;
            }

            var bodyStream = turboBody.GetResponseStream();
            if (bodyStream is not null)
            {
                var contentLength = ExtractContentLength(features.Get<IHttpResponseFeature>());
                if (!contentLength.HasValue)
                {
                    throw new InvalidOperationException(
                        "HTTP/1.0 requires a known Content-Length for response bodies. " +
                        "Set Content-Length in the response headers.");
                }

                // Known Content-Length: emit headers now, stream body via SerialBodyPump.
                // Create pump BEFORE encoding headers so EncodeDeferredResponse preserves
                // _deferredFeatures for OnResponseBodyComplete after streaming finishes.
                _serialPump = new SerialBodyPump(this, EnsureConnectionCts(), 16 * 1024, maxCapacity: 1);
                EncodeDeferredResponse(ReadOnlySpan<byte>.Empty);
                _serialPump.Register(bodyStream, contentLength, CancellationToken.None);
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
        if (name == DataRateCheck)
        {
            _rateTimerActive = false;
            _rateViolations.Clear();
            _requestRate.Check(Now(), _rateViolations);
            _responseRate.Check(Now(), _rateViolations);

            if (_rateViolations.Count > 0)
            {
                Tracing.For("Protocol").Warning(this,
                    "data rate violation (reqRate={0}, respRate={1})",
                    _requestRate.Count, _responseRate.Count);
                ShouldComplete = true;
                return;
            }

            if (_requestRate.Count > 0 || _responseRate.Count > 0)
            {
                EnsureRateTimer();
            }
        }
    }

    public void OnBodyMessage(object msg)
    {
        switch (msg)
        {
            case DrainReadComplete<int> read:
                _serialPump?.HandleReadComplete(read.BytesRead);
                break;

            case DrainReadFailed<int> failed:
                _serialPump?.HandleReadFailed(failed.Reason);
                break;

            case DrainContinue<int>:
                _serialPump?.HandleDrainContinue();
                break;
        }
    }

    private void EncodeDeferredResponse(ReadOnlySpan<byte> body)
    {
        if (_deferredFeatures is null)
        {
            return;
        }

        TransportBuffer? item = null;
        try
        {
            var bufferSize = 8 * 1024 + body.Length;
            item = TransportBuffer.Rent(bufferSize);
            var written = _encoder.EncodeDeferred(item.FullMemory.Span, _deferredFeatures, body);
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
        _ops.OnCancelTimer(DataRateCheck);
        _rateTimerActive = false;
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
