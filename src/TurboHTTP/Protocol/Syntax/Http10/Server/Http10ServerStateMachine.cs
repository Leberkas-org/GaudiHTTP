using System.Buffers;
using Akka.Actor;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;
using static Servus.Senf;

namespace TurboHTTP.Protocol.Syntax.Http10.Server;

internal readonly record struct ResponseBodyReadComplete(int BytesRead);

internal readonly record struct ResponseBodyReadFailed(Exception Reason);

internal readonly record struct ResponseBodyBuffered(IMemoryOwner<byte> Owner, int Written);

internal sealed class Http10ServerStateMachine : IServerStateMachine, IBodyDrainTarget
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

    private IFeatureCollection? _deferredFeatures;
    private BufferedBodyWriter? _activeBodyWriter;
    private Stream? _activeBodyStream;
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
        if (endStream)
        {
            _responseRate.Remove(0);
            if (_deferredFeatures is not null)
            {
                _ops.OnResponseBodyComplete(_deferredFeatures);
                _deferredFeatures = null;
            }

            Tracing.For("Protocol").Debug(this, "HTTP/1.0 response body complete (pump)");
            return;
        }

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

    void IBodyDrainTarget.OnDrainComplete(int streamId)
    {
        Tracing.For("Protocol").Debug(this, "HTTP/1.0 response body drain complete");
    }

    void IBodyDrainTarget.OnDrainFailed(int streamId, Exception reason)
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
                var outcome = _decoder.Feed(buffer.Memory[pos..], out var bodyConsumed);
                pos += bodyConsumed;
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

            if (result == DecodeOutcome.Complete || result == DecodeOutcome.HeadersReady)
            {
                var hasBody = result == DecodeOutcome.HeadersReady || _decoder.CurrentBodyReader is not null;
                var features = FeatureCollectionFactory.Create(_ops.PoolContext!, hasBody,
                    out var feature, _ops.Services, _ops.ConnectionFeature,
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
                        var bodyOutcome = _decoder.Feed(buffer.Memory[pos..], out var bodyConsumed);
                        pos += bodyConsumed;
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
                if (contentLength.HasValue)
                {
                    // Known Content-Length: emit headers now, stream body via SerialBodyPump.
                    // Create pump BEFORE encoding headers so EncodeDeferredResponse preserves
                    // _deferredFeatures for OnResponseBodyComplete after streaming finishes.
                    _serialPump = new SerialBodyPump(this, EnsureConnectionCts(), 16 * 1024, maxCapacity: 1);
                    EncodeDeferredResponse(ReadOnlySpan<byte>.Empty);
                    _serialPump.Register(bodyStream, contentLength, CancellationToken.None);
                    return;
                }

                // Unknown Content-Length: buffer entire body, then encode headers + body together.
                _activeBodyWriter = new BufferedBodyWriter();
                _activeBodyWriter.Reset(onComplete: (owner, written) =>
                {
                    _ops.StageActor.Tell(new ResponseBodyBuffered(owner, written), ActorRefs.NoSender);
                });
                _activeBodyStream = bodyStream;
                ReadNextResponseChunk();
                return;
            }
        }

        EncodeDeferredResponse(ReadOnlySpan<byte>.Empty);
    }

    private void ReadNextResponseChunk()
    {
        var mem = _activeBodyWriter!.GetMemory(16 * 1024);
        var vt = _activeBodyStream!.ReadAsync(mem);
        if (vt.IsCompletedSuccessfully)
        {
            HandleResponseBodyRead(vt.Result);
            return;
        }

        vt.PipeTo(
            _ops.StageActor,
            success: bytesRead => new ResponseBodyReadComplete(bytesRead),
            failure: ex => new ResponseBodyReadFailed(ex));
    }

    private void HandleResponseBodyRead(int bytesRead)
    {
        if (bytesRead > 0)
        {
            _responseRate.Observe(0, bytesRead, Now());
            EnsureRateTimer();
            if (_activeBodyWriter is not null)
            {
                _activeBodyWriter.Advance(bytesRead);
                _activeBodyWriter.FlushAsync();
                ReadNextResponseChunk();
            }
        }
        else
        {
            _activeBodyWriter?.CompleteAsync();
            // Response fully handed to the transport: drop the rate entry so a keep-alive
            // connection is not flagged as a violation once the grace period elapses.
            _responseRate.Remove(0);
        }
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
            // SerialBodyPump messages (known Content-Length path)
            case DrainReadComplete read:
                _serialPump?.HandleReadComplete(read.BytesRead);
                break;

            case DrainReadFailed failed:
                _serialPump?.HandleReadFailed(failed.Reason);
                break;

            case DrainContinue:
                _serialPump?.HandleDrainContinue();
                break;

            // BufferedBodyWriter messages (unknown Content-Length path)
            case ResponseBodyReadComplete read:
                HandleResponseBodyRead(read.BytesRead);
                break;

            case ResponseBodyBuffered bufferDone:
                var body = bufferDone.Owner.Memory.Span[..bufferDone.Written];
                EncodeDeferredResponse(body);
                bufferDone.Owner.Dispose();
                _activeBodyWriter = null;
                _activeBodyStream = null;
                _responseRate.Remove(0);
                break;

            case ResponseBodyReadFailed failed:
                Tracing.For("Protocol").Warning(this, "response body failed: {0}", failed.Reason.Message);
                _activeBodyWriter?.Dispose();
                _activeBodyWriter = null;
                _activeBodyStream = null;
                _responseRate.Remove(0);
                ShouldComplete = true;
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
        _activeBodyWriter?.Dispose();
        _activeBodyWriter = null;
        _activeBodyStream = null;
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
