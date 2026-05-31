using System.Buffers;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Server;
using TurboHTTP.Protocol.Syntax.Http10.Options;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;
using static Servus.Core.Servus;
using HttpVersion = System.Net.HttpVersion;


namespace TurboHTTP.Protocol.Syntax.Http10.Server;

internal sealed class Http10ServerStateMachine : IServerStateMachine
{
    private readonly IServerStageOperations _ops;
    private readonly Http10ServerDecoder _decoder;
    private readonly Http10ServerEncoder _encoder;
    private readonly long _maxRequestBodySize;
    private readonly int _responseBodyChunkSize;
    private readonly DataRateMonitor _requestRate;
    private readonly DataRateMonitor _responseRate;
    private readonly Func<long> _now;

    private IFeatureCollection? _deferredFeatures;
    private IMemoryOwner<byte>? _deferredBodyOwner;
    private int _deferredBodyLength;
    private IBodyEncoder? _activeBodyEncoder;

    public bool CanAcceptResponse => true;
    public bool ShouldComplete { get; private set; }

    public int MaxQueuedRequests => 1;

    public Http10ServerStateMachine(Http1ConnectionOptions options, IServerStageOperations ops, Func<long>? clock = null)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(options);
        _maxRequestBodySize = options.Limits.MaxRequestBodySize;
        _responseBodyChunkSize = options.ResponseBodyChunkSize;
        _now = clock ?? (() => Environment.TickCount64);

        var rate = options.ToRateMonitor();
        _requestRate = new DataRateMonitor(rate.MinRequestBodyDataRate, rate.MinRequestBodyDataRateGracePeriod);
        _responseRate = new DataRateMonitor(rate.MinResponseDataRate, rate.MinResponseDataRateGracePeriod);

        _decoder = new Http10ServerDecoder(options.ToHttp10DecoderOptions());
        _encoder = new Http10ServerEncoder(options.ToHttp10EncoderOptions());
    }

    public void PreStart()
    {
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

            var outcome = _decoder.Feed(buffer.Memory.Span, out _);

            // Observe request body bytes if body decoder is active
            if (_decoder.LastBodyBytesConsumed > 0)
            {
                _requestRate.Observe(0, _decoder.LastBodyBytesConsumed, _now());
                EnsureRateTimer();
            }

            if (outcome == DecodeOutcome.Complete)
            {
                var feature = _decoder.GetRequestFeature();
                var hasBody = feature.Body != Stream.Null;
                var features = FeatureCollectionFactory.Create(feature, hasBody, _ops.Services, _ops.ConnectionFeature, _ops.TlsHandshakeFeature, _maxRequestBodySize);
                _requestRate.Remove(0);
                _ops.OnRequest(features);
            }
        }
        catch (Exception)
        {
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
            var bodyStream = turboBody.GetResponseStream();
            var encoder = BodyEncoderFactory.Create(bodyStream, null, HttpVersion.Version10, new BodyEncoderOptions { ChunkSize = _responseBodyChunkSize });
            if (encoder is not null)
            {
                _activeBodyEncoder = encoder;
                encoder.Start(bodyStream!, _ops.StageActor);
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
        if (name == "data-rate-check")
        {
            var violations = new List<long>();
            _requestRate.Check(_now(), violations);
            _responseRate.Check(_now(), violations);

            if (violations.Count > 0)
            {
                ShouldComplete = true;
                return;
            }

            if (_requestRate.Count > 0 || _responseRate.Count > 0)
            {
                _ops.OnScheduleTimer("data-rate-check", TimeSpan.FromSeconds(1));
            }
        }
    }

    public void OnBodyMessage(object msg)
    {

        switch (msg)
        {
            case OutboundBodyChunk chunk when _deferredFeatures is not null:
                _deferredBodyOwner?.Dispose();
                _deferredBodyOwner = chunk.Owner;
                _deferredBodyLength = chunk.Length;
                // Observe response body bytes as chunks arrive
                if (chunk.Length > 0)
                {
                    _responseRate.Observe(0, chunk.Length, _now());
                    EnsureRateTimer();
                }
                break;

            case OutboundBodyComplete when _deferredFeatures is not null:
                var body = _deferredBodyOwner is not null
                    ? _deferredBodyOwner.Memory.Span[.._deferredBodyLength]
                    : ReadOnlySpan<byte>.Empty;
                EncodeDeferredResponse(body);
                _deferredBodyOwner?.Dispose();
                _deferredBodyOwner = null;
                _responseRate.Remove(0);
                break;

            case OutboundBodyFailed failed:
                _deferredBodyOwner?.Dispose();
                _deferredBodyOwner = null;
                if (_deferredFeatures is not null)
                {
                    Tracing.For("Protocol").Error(this, "Failed to read HTTP/1.0 response body: {0}", failed.Reason.Message);
                    _deferredFeatures = null;
                    ShouldComplete = true;
                }
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
            var bufferSize = 8192 + body.Length;
            item = TransportBuffer.Rent(bufferSize);
            var written = _encoder.EncodeDeferred(item.FullMemory.Span, _deferredFeatures, body);
            item.Length = written;

            _ops.OnOutbound(new TransportData(item));
        }
        catch (Exception ex)
        {
            item?.Dispose();

            Tracing.For("Protocol").Error(this, "Failed to encode HTTP/1.0 response: {0}", ex.Message);
        }
        finally
        {
            _deferredFeatures = null;
        }
    }

    public void Cleanup()
    {
        _activeBodyEncoder?.Dispose();
        _activeBodyEncoder = null;
        _deferredBodyOwner?.Dispose();
        _deferredBodyOwner = null;
        _deferredFeatures = null;
        _ops.OnCancelTimer("data-rate-check");
    }

    private void EnsureRateTimer() => _ops.OnScheduleTimer("data-rate-check", TimeSpan.FromSeconds(1));
}
