using System.Net;
using Akka.Event;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.LineBased.Body;
using TurboHTTP.Protocol.Server;
using TurboHTTP.Protocol.Syntax.Http11.Options;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Protocol.Syntax.Http11.Server;

internal sealed class Http11ServerStateMachine : IServerStateMachine
{
    private readonly IServerStageOperations _ops;
    private readonly Http11ServerDecoder _decoder;
    private readonly Http11ServerEncoder _encoder;
    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _requestHeadersTimeout;

    private readonly TimeSpan _bodyConsumptionTimeout;
    private readonly TimeSpan _bodyReadTimeout;
    private readonly int _responseBodyChunkSize;
    private readonly long _maxRequestBodySize;
    private readonly Http2ConnectionOptions _h2UpgradeOptions;

    private readonly DataRateMonitor _requestRate;
    private readonly DataRateMonitor _responseRate;
    private readonly Func<long> _now;

    private int _pendingResponseCount;
    private bool _outboundBodyPending;
    private bool _requestHeadersTimerActive;
    private bool _bodyReadTimerActive;
    private bool _draining;
    private bool _bodyStreaming;

    public bool CanAcceptResponse => !_outboundBodyPending && _pendingResponseCount > 0;
    public bool ShouldComplete { get; private set; }
    public int MaxQueuedRequests { get; }

    public Http11ServerStateMachine(Http1ConnectionOptions options, Http2ConnectionOptions h2UpgradeOptions, IServerStageOperations ops, Func<long>? clock = null)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(h2UpgradeOptions);
        _h2UpgradeOptions = h2UpgradeOptions;
        _bodyConsumptionTimeout = options.BodyConsumptionTimeout;
        _bodyReadTimeout = options.BodyReadTimeout;
        _responseBodyChunkSize = options.ResponseBodyChunkSize;
        _maxRequestBodySize = options.Limits.MaxRequestBodySize;
        _now = clock ?? (() => Environment.TickCount64);

        var rate = options.ToRateMonitor();
        _requestRate = new DataRateMonitor(rate.MinRequestBodyDataRate, rate.MinRequestBodyDataRateGracePeriod);
        _responseRate = new DataRateMonitor(rate.MinResponseDataRate, rate.MinResponseDataRateGracePeriod);

        var decOpts = options.ToHttp11DecoderOptions();
        var encOpts = options.ToHttp11EncoderOptions();

        if (decOpts.MaxPipelinedRequests <= 0)
        {
            throw new ArgumentException("MaxPipelinedRequests must be greater than zero.", nameof(options));
        }

        _decoder = new Http11ServerDecoder(decOpts);
        _encoder = new Http11ServerEncoder(encOpts);
        _keepAliveTimeout = encOpts.KeepAliveTimeout;
        _requestHeadersTimeout = encOpts.RequestHeadersTimeout;
        MaxQueuedRequests = decOpts.MaxPipelinedRequests;
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
            var span = buffer.Memory.Span;
            var pos = 0;

            if (_draining && _decoder.CurrentBodyDecoder is { } drainingDecoder)
            {
                var drained = drainingDecoder.Drain(span[pos..]);
                pos += drained;
                _requestRate.Observe(0, drained, _now());
                EnsureRateTimer();

                if (drainingDecoder.IsComplete)
                {
                    _draining = false;
                    _ops.OnCancelTimer("body-consumption");
                    _requestRate.Remove(0);
                    _decoder.Reset();
                }
            }
            else if (_bodyStreaming && _decoder.CurrentBodyDecoder is { } streamingDecoder)
            {
                var done = streamingDecoder.Feed(span[pos..], out var bConsumed);
                pos += bConsumed;
                _requestRate.Observe(0, bConsumed, _now());
                EnsureRateTimer();

                if (done)
                {
                    _bodyStreaming = false;
                    _requestRate.Remove(0);
                    _decoder.Reset();
                }
            }

            // Schedule request headers timeout if not already active
            if (!_requestHeadersTimerActive && _pendingResponseCount == 0 && !_bodyStreaming && _requestHeadersTimeout > TimeSpan.Zero)
            {
                _ops.OnScheduleTimer("request-headers", _requestHeadersTimeout);
                _requestHeadersTimerActive = true;
            }

            while (pos < span.Length && !_bodyStreaming)
            {
                var outcome = _decoder.Feed(span[pos..], out var consumed);
                pos += consumed;

                if (outcome == DecodeOutcome.NeedMore)
                {
                    break;
                }

                // Cancel the request headers timer once headers are complete
                if (_requestHeadersTimerActive)
                {
                    _ops.OnCancelTimer("request-headers");
                    _requestHeadersTimerActive = false;
                }

                // Limit *in-flight* (pipelined, not-yet-answered) requests, not the cumulative
                // total over the connection. _pendingResponseCount is incremented when a request
                // is dispatched and decremented in OnResponse, so it is the live pipeline depth.
                if (_pendingResponseCount >= MaxQueuedRequests)
                {
                    ShouldComplete = true;
                    break;
                }

                if (!ShouldComplete && _decoder.HasConnectionClose)
                {
                    ShouldComplete = true;
                }

                var feature = _decoder.GetRequestFeature();
                var hasBody = outcome == DecodeOutcome.HeadersReady || feature.Body != Stream.Null;
                var features = FeatureCollectionFactory.Create(feature, hasBody, _ops.Services, _ops.ConnectionFeature,
                    _ops.TlsHandshakeFeature, _maxRequestBodySize);

                if (!ShouldComplete && feature.Protocol == "HTTP/1.0")
                {
                    ShouldComplete = true;
                }

                if (TryHandleH2cUpgrade(features))
                {
                    _decoder.Reset();
                    break;
                }

                _pendingResponseCount++;
                _ops.OnRequest(features);

                if (outcome == DecodeOutcome.HeadersReady)
                {
                    _bodyStreaming = true;

                    if (pos < span.Length)
                    {
                        var bodyDone = _decoder.CurrentBodyDecoder!.Feed(span[pos..], out var bConsumed);
                        pos += bConsumed;
                        _requestRate.Observe(0, bConsumed, _now());
                        EnsureRateTimer();
                        if (bodyDone)
                        {
                            _bodyStreaming = false;
                            _requestRate.Remove(0);
                            _decoder.Reset();
                            continue;
                        }
                    }

                    break;
                }

                _decoder.Reset();
            }

            // While an inbound request body is still streaming in, enforce an idle
            // gap between body reads. Each inbound packet re-arms the timer (the ops
            // layer de-duplicates by name); when the body completes it is cancelled.
            ReconcileBodyReadTimer();
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

    private void ReconcileBodyReadTimer()
    {
        if (_bodyStreaming && _bodyReadTimeout > TimeSpan.Zero)
        {
            _ops.OnScheduleTimer("body-read", _bodyReadTimeout);
            _bodyReadTimerActive = true;
        }
        else if (_bodyReadTimerActive)
        {
            _ops.OnCancelTimer("body-read");
            _bodyReadTimerActive = false;
        }
    }

    public void OnResponse(IFeatureCollection features)
    {
        if (_pendingResponseCount == 0)
        {
            throw new InvalidOperationException("Cannot send a response when no requests are pending.");
        }

        _pendingResponseCount--;

        var responseFeature = features.Get<IHttpResponseFeature>();
        var responseBody = features.Get<IHttpResponseBodyFeature>();

        var statusCode = responseFeature?.StatusCode ?? 200;
        var suppressBody = statusCode is >= 100 and < 200 or 204 or 304;

        var contentLength = ExtractContentLength(responseFeature);
        var hasExplicitChunked = responseFeature?.Headers?.Any(h =>
            h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
            && h.Value.Any(v => v.Equals(WellKnownHeaders.ChunkedValue, StringComparison.OrdinalIgnoreCase))) ?? false;
        var isChunked = !suppressBody && (contentLength is null || hasExplicitChunked);

        var responseBuffer = TransportBuffer.Rent(8192);
        var span = responseBuffer.FullMemory.Span;
        var written = _encoder.Encode(span, features, isChunked, connectionClose: ShouldComplete);
        responseBuffer.Length = written;
        _ops.OnOutbound(new TransportData(responseBuffer));

        if (suppressBody)
        {
            if (!ShouldComplete && _keepAliveTimeout > TimeSpan.Zero && _pendingResponseCount == 0)
            {
                _ops.OnScheduleTimer("keep-alive", _keepAliveTimeout);
            }

            return;
        }

        if (_decoder.CurrentBodyDecoder is { IsComplete: false })
        {
            if (_bodyStreaming)
            {
                _bodyStreaming = false;
                if (_bodyReadTimerActive)
                {
                    _ops.OnCancelTimer("body-read");
                    _bodyReadTimerActive = false;
                }
            }

            _draining = true;

            if (_bodyConsumptionTimeout > TimeSpan.Zero)
            {
                _ops.OnScheduleTimer("body-consumption", _bodyConsumptionTimeout);
            }
        }

        if (responseBody is TurboHttpResponseBodyFeature turboBody)
        {
            _outboundBodyPending = true;

            var bodyStream = turboBody.GetResponseStream();
            var encoder = BodyEncoderFactory.Create(bodyStream, contentLength, HttpVersion.Version11, new BodyEncoderOptions { ChunkSize = _responseBodyChunkSize });
            if (encoder is not null)
            {
                _encoder.SetActiveBodyEncoder(encoder);
                encoder.Start(bodyStream, _ops.StageActor);
            }
        }
        else
        {
            if (!ShouldComplete && _keepAliveTimeout > TimeSpan.Zero && _pendingResponseCount == 0)
            {
                _ops.OnScheduleTimer("keep-alive", _keepAliveTimeout);
            }
        }
    }

    public void OnDownstreamFinished()
    {
    }

    public void OnTimerFired(string name)
    {
        if (name == "keep-alive")
        {
            ShouldComplete = true;
        }
        else if (name == "request-headers")
        {
            _requestHeadersTimerActive = false;
            ShouldComplete = true;
        }
        else if (name == "body-consumption")
        {
            _draining = false;
            ShouldComplete = true;
        }
        else if (name == "body-read")
        {
            _bodyReadTimerActive = false;
            ShouldComplete = true;
        }
        else if (name == "data-rate-check")
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
            case OutboundBodyChunk chunk:
                // Observe response body bytes before sending
                _responseRate.Observe(0, chunk.Length, _now());
                EnsureRateTimer();
                // Hand the chunk's pooled buffer straight to the transport — no rent + copy.
                _ops.OnOutbound(new TransportData(TransportBuffer.Wrap(chunk.Owner, chunk.Length)));
                break;

            case OutboundBodyComplete:
                _outboundBodyPending = false;
                _responseRate.Remove(0);
                // Schedule keep-alive timer after body completes if needed
                if (!ShouldComplete && _keepAliveTimeout > TimeSpan.Zero && _pendingResponseCount == 0)
                {
                    _ops.OnScheduleTimer("keep-alive", _keepAliveTimeout);
                }

                break;

            case OutboundBodyFailed failed:
                _outboundBodyPending = false;
                _responseRate.Remove(0);
                _ops.Log.Warning("Failed to encode HTTP/1.1 response body: {0}", failed.Reason.Message);
                break;
        }
    }

    private static long? ExtractContentLength(IHttpResponseFeature? responseFeature)
    {
        if (responseFeature?.Headers is null)
        {
            return null;
        }

        foreach (var header in responseFeature.Headers)
        {
            if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                if (header.Value.FirstOrDefault() is { } value && long.TryParse(value, out var length))
                {
                    return length;
                }
            }
        }

        return null;
    }

    private bool TryHandleH2cUpgrade(IFeatureCollection features)
    {
        if (_ops is not IProtocolSwitchCapable switchable)
        {
            return false;
        }

        var requestFeature = features.Get<IHttpRequestFeature>();
        var requestHeaders = requestFeature?.Headers;
        if (requestHeaders is null)
        {
            return false;
        }

        var hasUpgrade = requestHeaders.TryGetValue("Upgrade", out var upgradeValue)
                         && !string.IsNullOrEmpty(upgradeValue)
                         && upgradeValue.ToString().Split(',')
                             .Any(v => v.Trim().Equals("h2c", StringComparison.OrdinalIgnoreCase));

        if (!hasUpgrade)
        {
            return false;
        }

        if (!requestHeaders.TryGetValue("HTTP2-Settings", out _))
        {
            return false;
        }

        var responseBytes = "HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: h2c\r\n\r\n"u8;
        var responseBuffer = TransportBuffer.Rent(responseBytes.Length);
        responseBytes.CopyTo(responseBuffer.FullMemory.Span);
        responseBuffer.Length = responseBytes.Length;
        _ops.OnOutbound(new TransportData(responseBuffer));

        switchable.RequestProtocolSwitch(ops => new Http2ServerStateMachine(_h2UpgradeOptions, ops));

        return true;
    }

    public void Cleanup()
    {
        _encoder.CancelActiveBody();
        _outboundBodyPending = false;
        _pendingResponseCount = 0;
        if (_requestHeadersTimerActive)
        {
            _ops.OnCancelTimer("request-headers");
            _requestHeadersTimerActive = false;
        }

        if (_bodyReadTimerActive)
        {
            _ops.OnCancelTimer("body-read");
            _bodyReadTimerActive = false;
        }

        _ops.OnCancelTimer("keep-alive");
        _ops.OnCancelTimer("body-consumption");
        _ops.OnCancelTimer("data-rate-check");
    }

    private void EnsureRateTimer() => _ops.OnScheduleTimer("data-rate-check", TimeSpan.FromSeconds(1));
}