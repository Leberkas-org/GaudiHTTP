using System.Buffers;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Akka.Actor;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;
using static Servus.Senf;

namespace TurboHTTP.Protocol.Syntax.Http11.Server;

internal sealed class Http11ServerStateMachine : IServerStateMachine, IBodyDrainTarget, IBodyDrainTarget<int>
{
    private const string KeepAliveTimer = "keep-alive";
    private const string RequestHeadersTimer = "request-headers";
    private const string BodyConsumptionTimer = "body-consumption";
    private const string BodyReadTimer = "body-read";
    private const string DataRateCheck = "data-rate-check";

    private readonly IServerStageOperations _ops;
    private readonly Http11ServerDecoder _decoder;
    private readonly Http11ServerEncoder _encoder;
    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _requestHeadersTimeout;

    private readonly TimeSpan _bodyConsumptionTimeout;
    private readonly TimeSpan _bodyReadTimeout;
    private readonly BodyEncoderOptions _bodyEncoderOptions;
    private readonly long _maxRequestBodySize;
    private readonly Http2ConnectionOptions _h2UpgradeOptions;

    private readonly DataRateMonitor _requestRate;
    private readonly DataRateMonitor _responseRate;
    private readonly List<long> _rateViolations = [];
    private bool _rateTimerActive;
    private readonly TimeProvider _clock;

    private long Now() => _clock.GetUtcNow().ToUnixTimeMilliseconds();

    private int _pendingResponseCount;
    private bool _outboundBodyPending;
    private bool _requestHeadersTimerActive;
    private bool _bodyReadTimerActive;
    private bool _draining;
    private bool _bodyStreaming;
    private IStreamingBodyReader? _activeStreamingReader;

    private readonly ConnectionBodyPool _pool = new();
    private Func<IMemoryOwner<byte>, ReadOnlyMemory<byte>, ValueTask>? _cachedSendDelegate;
    private IBodyWriter? _activeResponseBodyWriter;
    private IFeatureCollection? _activeResponseFeatures;
    private SerialBodyPump? _serialPump;
    private CancellationTokenSource? _connectionCts;

    public bool CanAcceptResponse => !_outboundBodyPending && _pendingResponseCount > 0;
    public bool ShouldComplete { get; private set; }
    public bool ShouldPauseNetwork => _activeStreamingReader?.IsFull ?? false;
    public int MaxQueuedRequests { get; }

    // HTTP/1.1 responses are matched to requests by position on the wire, so a pipelined request
    // must not be dispatched to the handler until the previous response has been emitted
    // (RFC 9112 §9.3.2). One-at-a-time dispatch keeps the shared bridge from reordering responses.
    public int MaxConcurrentRequests => 1;

    public Http11ServerStateMachine(Http1ConnectionOptions options, Http2ConnectionOptions h2UpgradeOptions,
        IServerStageOperations ops, TimeProvider? timeProvider = null)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(h2UpgradeOptions);
        _h2UpgradeOptions = h2UpgradeOptions;
        _bodyConsumptionTimeout = options.BodyConsumptionTimeout;
        _bodyReadTimeout = options.BodyReadTimeout;
        _bodyEncoderOptions = options.ToBodyEncoderOptions();
        _maxRequestBodySize = options.Limits.MaxRequestBodySize;
        _clock = timeProvider ?? TimeProvider.System;

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

    private CancellationTokenSource EnsureConnectionCts()
    {
        return _connectionCts ??= new CancellationTokenSource();
    }

    IActorRef IBodyDrainTarget.StageActor => _ops.StageActor;
    IActorRef IBodyDrainTarget<int>.StageActor => _ops.StageActor;

    void IBodyDrainTarget<int>.EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
        => ((IBodyDrainTarget)this).EmitDataFrames(streamId, data, endStream);

    void IBodyDrainTarget<int>.OnDrainComplete(int streamId)
        => ((IBodyDrainTarget)this).OnDrainComplete(streamId);

    void IBodyDrainTarget<int>.OnDrainFailed(int streamId, Exception reason)
        => ((IBodyDrainTarget)this).OnDrainFailed(streamId, reason);

    void IBodyDrainTarget.EmitDataFrames(int streamId, ReadOnlyMemory<byte> data, bool endStream)
    {
        if (endStream)
        {
            _activeResponseBodyWriter!.CompleteAsync();
            _outboundBodyPending = false;
            _activeResponseBodyWriter = null;
            _responseRate.Remove(0);
            if (_activeResponseFeatures is not null)
            {
                _ops.OnResponseBodyComplete(_activeResponseFeatures);
                _activeResponseFeatures = null;
            }

            Tracing.For("Protocol").Debug(this, "response body complete");
            if (!ShouldComplete && _keepAliveTimeout > TimeSpan.Zero && _pendingResponseCount == 0)
            {
                _ops.OnScheduleTimer(KeepAliveTimer, _keepAliveTimeout);
            }

            return;
        }

        var dest = _activeResponseBodyWriter!.GetMemory(data.Length);
        data.CopyTo(dest);
        _activeResponseBodyWriter.Advance(data.Length);
        _activeResponseBodyWriter.FlushAsync();
        Tracing.For("Protocol").Trace(this, "response body chunk flushed (bytes={0})", data.Length);
    }

    void IBodyDrainTarget.OnDrainComplete(int streamId)
    {
        Tracing.For("Protocol").Debug(this, "response body drain complete");
    }

    void IBodyDrainTarget.OnDrainFailed(int streamId, Exception reason)
    {
        _outboundBodyPending = false;
        _activeResponseBodyWriter?.Dispose();
        _activeResponseBodyWriter = null;
        _responseRate.Remove(0);
        if (_activeResponseFeatures is not null)
        {
            _ops.OnResponseBodyComplete(_activeResponseFeatures);
            _activeResponseFeatures = null;
        }

        Tracing.For("Protocol").Warning(this, "response body failed: {0}", reason.Message);
    }

    public void DecodeClientData(ITransportInbound data)
    {
        if (data is not TransportData { Buffer: var buffer })
        {
            return;
        }

        if (buffer.Length == 0)
        {
            return;
        }

        try
        {
            var span = buffer.Memory.Span;
            var pos = 0;

            if (_draining && _decoder.CurrentFramingDecoder is { } drainingDecoder)
            {
                var drained = drainingDecoder.Drain(span[pos..]);
                pos += drained;
                _requestRate.Observe(0, drained, Now());
                EnsureRateTimer();

                if (drainingDecoder.IsComplete)
                {
                    _draining = false;
                    _ops.OnCancelTimer(BodyConsumptionTimer);
                    _requestRate.Remove(0);
                    _decoder.Reset();
                }
            }
            else if (_bodyStreaming && _decoder.StreamingReader is not null)
            {
                var outcome = _decoder.Feed(buffer.Memory[pos..], out var bodyConsumed);
                pos += bodyConsumed;
                _requestRate.Observe(0, bodyConsumed, Now());
                EnsureRateTimer();

                if (outcome == DecodeOutcome.Complete)
                {
                    _bodyStreaming = false;
                    _activeStreamingReader = null;
                    _requestRate.Remove(0);
                    _decoder.Reset();
                }
            }

            if (!_requestHeadersTimerActive && _pendingResponseCount == 0 && !_bodyStreaming
                && !_outboundBodyPending
                && _requestHeadersTimeout > TimeSpan.Zero)
            {
                _ops.OnScheduleTimer(RequestHeadersTimer, _requestHeadersTimeout);
                _requestHeadersTimerActive = true;
                Tracing.For("Protocol").Debug(this, "request headers timer scheduled ({0}ms)",
                    _requestHeadersTimeout.TotalMilliseconds);
            }

            while (pos < span.Length && !_bodyStreaming)
            {
                var outcome = _decoder.Feed(buffer.Memory[pos..], out var consumed);
                pos += consumed;

                if (outcome == DecodeOutcome.NeedMore)
                {
                    break;
                }

                if (_requestHeadersTimerActive)
                {
                    _ops.OnCancelTimer(RequestHeadersTimer);
                    _requestHeadersTimerActive = false;
                    Tracing.For("Protocol").Debug(this, "request headers timer cancelled (headers complete)");
                }

                if (_pendingResponseCount >= MaxQueuedRequests)
                {
                    ShouldComplete = true;
                    break;
                }

                if (!ShouldComplete && _decoder.HasConnectionClose)
                {
                    ShouldComplete = true;
                }

                var hasBody = outcome == DecodeOutcome.HeadersReady || _decoder.CurrentBodyReader is not null;
                var features = FeatureCollectionFactory.Create(_ops.PoolContext!, hasBody,
                    out var feature, _ops.ConnectionFeature,
                    _ops.TlsHandshakeFeature, _maxRequestBodySize);
                _decoder.PopulateRequestFeature(feature);

                if (!ShouldComplete && feature.Protocol == WellKnownHeaders.Http10)
                {
                    ShouldComplete = true;
                }

                if (TryHandleH2cUpgrade(features))
                {
                    _decoder.Reset();
                    break;
                }

                _pendingResponseCount++;
                Tracing.For("Protocol").Debug(this, "request dispatched (pending={0})", _pendingResponseCount);
                _ops.OnRequest(features);

                if (outcome == DecodeOutcome.HeadersReady)
                {
                    _bodyStreaming = true;
                    Tracing.For("Protocol").Trace(this, "request body streaming started");

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
                        _requestRate.Observe(0, bodyConsumed, Now());
                        EnsureRateTimer();

                        if (bodyOutcome == DecodeOutcome.Complete)
                        {
                            _bodyStreaming = false;
                            _activeStreamingReader = null;
                            _requestRate.Remove(0);
                            _decoder.Reset();
                            continue;
                        }
                    }

                    break;
                }

                _decoder.Reset();
            }

            ReconcileBodyReadTimer();
        }
        catch (Exception ex)
        {
            Tracing.For("Protocol").Warning(this, "Failed to decode HTTP/1.1 request: {0}", ex.Message);
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
            _ops.OnScheduleTimer(BodyReadTimer, _bodyReadTimeout);
            _bodyReadTimerActive = true;
        }
        else if (_bodyReadTimerActive)
        {
            _ops.OnCancelTimer(BodyReadTimer);
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
        Tracing.For("Protocol").Debug(this, "response received (status={0}, pending={1})",
            features.Get<IHttpResponseFeature>()?.StatusCode ?? 0, _pendingResponseCount);

        var responseFeature = features.Get<IHttpResponseFeature>();
        var responseBody = features.Get<IHttpResponseBodyFeature>();

        var statusCode = responseFeature?.StatusCode ?? 200;
        // A response to HEAD carries the same headers a GET would (Content-Length/Transfer-Encoding
        // are still emitted) but MUST NOT include a body — emitting one desynchronizes the keep-alive
        // connection (RFC 9110 §9.3.2, RFC 9112 §6.3). The request method rides on the same feature
        // collection the bridge echoes back, so it's available here.
        var isHeadRequest = string.Equals(
            features.Get<IHttpRequestFeature>()?.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
        var suppressBody = isHeadRequest || statusCode is >= 100 and < 200 or 204 or 304;

        var contentLength = ExtractContentLength(responseFeature);
        var hasExplicitChunked = false;
        if (responseFeature?.Headers is { } responseHeaders)
        {
            foreach (var h in responseHeaders)
            {
                if (!h.Key.Equals(WellKnownHeaders.TransferEncoding, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var v in h.Value)
                {
                    if (v != null && v.Equals(WellKnownHeaders.ChunkedValue, StringComparison.OrdinalIgnoreCase))
                    {
                        hasExplicitChunked = true;
                        break;
                    }
                }

                if (hasExplicitChunked)
                {
                    break;
                }
            }
        }

        var isChunked = !suppressBody && (contentLength is null || hasExplicitChunked);

        var estimatedSize = EstimateResponseHeaderSize(responseFeature);
        var responseBuffer = TransportBuffer.Rent(estimatedSize);
        var span = responseBuffer.FullMemory.Span;
        var written = _encoder.Encode(span, features, isChunked, connectionClose: ShouldComplete);
        responseBuffer.Length = written;
        _ops.OnOutbound(TransportData.Rent(responseBuffer));

        if (suppressBody)
        {
            // Headers-only response (1xx/204/304 or HEAD): no body drain will run, so recycle the
            // feature collection now. Safe — the SM keeps no reference to `features` on this path.
            _ops.OnResponseBodyComplete(features);

            if (!ShouldComplete && _keepAliveTimeout > TimeSpan.Zero && _pendingResponseCount == 0)
            {
                _ops.OnScheduleTimer(KeepAliveTimer, _keepAliveTimeout);
            }

            return;
        }

        if (_decoder.CurrentBodyReader is { IsCompleted: false })
        {
            if (_bodyStreaming)
            {
                _bodyStreaming = false;
                _activeStreamingReader = null;
                if (_bodyReadTimerActive)
                {
                    _ops.OnCancelTimer(BodyReadTimer);
                    _bodyReadTimerActive = false;
                }
            }

            _draining = true;
            Tracing.For("Protocol").Debug(this, "draining unconsumed request body");

            if (_bodyConsumptionTimeout > TimeSpan.Zero)
            {
                _ops.OnScheduleTimer(BodyConsumptionTimer, _bodyConsumptionTimeout);
            }
        }

        if (responseBody is TurboHttpResponseBodyFeature turboBody)
        {
            if (turboBody.TryGetBufferedBody(out var bufferedBody))
            {
                EmitBufferedBody(features, bufferedBody, contentLength);
                return;
            }

            _outboundBodyPending = true;
            _activeResponseFeatures = features;
            Tracing.For("Protocol").Debug(this, "response body writer starting (chunked={0})", isChunked);

            var bodyStream = turboBody.GetResponseStream();
            var (writer, _) = _pool.RentWriter(
                hasBody: true, contentLength, HttpVersion.Version11, _bodyEncoderOptions,
                send: GetSendDelegate());

            _activeResponseBodyWriter = writer;

            _serialPump =
                new SerialBodyPump(this, EnsureConnectionCts(), _bodyEncoderOptions.ChunkSize, maxCapacity: 2);
            _serialPump.Register(bodyStream, contentLength, CancellationToken.None);
        }
        else
        {
            // No streamed body feature to drain: recycle the feature collection now.
            _ops.OnResponseBodyComplete(features);

            if (!ShouldComplete && _keepAliveTimeout > TimeSpan.Zero && _pendingResponseCount == 0)
            {
                _ops.OnScheduleTimer(KeepAliveTimer, _keepAliveTimeout);
            }
        }
    }

    private void EmitBufferedBody(IFeatureCollection features, ReadOnlyMemory<byte> body, long? contentLength)
    {
        var (writer, _) = _pool.RentWriter(
            hasBody: true, contentLength, HttpVersion.Version11, _bodyEncoderOptions,
            send: GetSendDelegate());

        if (body.Length > 0)
        {
            var remaining = body;
            while (remaining.Length > 0)
            {
                var take = Math.Min(remaining.Length, _bodyEncoderOptions.ChunkSize);
                var dest = writer.GetMemory(take);
                remaining.Span[..take].CopyTo(dest.Span);
                writer.Advance(take);
                writer.FlushAsync();
                remaining = remaining[take..];
            }
        }

        writer.CompleteAsync();
        // The response is fully handed to the transport: drop the rate entry, or the idle
        // keep-alive connection is flagged as a violation once the grace period elapses.
        _responseRate.Remove(0);
        _ops.OnResponseBodyComplete(features);

        Tracing.For("Protocol").Debug(this, "response body complete (buffered, bytes={0})", body.Length);
        if (!ShouldComplete && _keepAliveTimeout > TimeSpan.Zero && _pendingResponseCount == 0)
        {
            _ops.OnScheduleTimer(KeepAliveTimer, _keepAliveTimeout);
        }
    }

    public void OnDownstreamFinished()
    {
    }

    public void OnTimerFired(string name)
    {
        if (name == KeepAliveTimer)
        {
            Tracing.For("Protocol").Info(this, "keep-alive timeout — closing connection");
            ShouldComplete = true;
        }
        else if (name == RequestHeadersTimer)
        {
            Tracing.For("Protocol").Info(this,
                "request headers timeout (outboundBodyPending={0}, pending={1})",
                _outboundBodyPending, _pendingResponseCount);
            _requestHeadersTimerActive = false;
            ShouldComplete = true;
        }
        else if (name == BodyConsumptionTimer)
        {
            Tracing.For("Protocol").Info(this, "body consumption timeout — closing connection");
            _draining = false;
            ShouldComplete = true;
        }
        else if (name == BodyReadTimer)
        {
            Tracing.For("Protocol").Info(this, "body read timeout — closing connection");
            _bodyReadTimerActive = false;
            ShouldComplete = true;
        }
        else if (name == DataRateCheck)
        {
            _rateTimerActive = false;
            _rateViolations.Clear();
            _requestRate.Check(Now(), _rateViolations);
            _responseRate.Check(Now(), _rateViolations);

            if (_rateViolations.Count > 0)
            {
                Tracing.For("Protocol").Warning(this,
                    "data rate violation (reqRate={0}, respRate={1}, paused={2})",
                    _requestRate.Count, _responseRate.Count, ShouldPauseNetwork);
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
            case DrainReadComplete read:
                _serialPump?.HandleReadComplete(read.BytesRead);
                break;

            case DrainReadFailed failed:
                _serialPump?.HandleReadFailed(failed.Reason);
                break;

            case DrainContinue:
                _serialPump?.HandleDrainContinue();
                break;
        }
    }

    public void OnOutboundFlushed()
    {
        if (_serialPump is not null)
        {
            _serialPump.ResetSyncReadCounter();
            _serialPump.OnCapacityAvailable();
        }
    }

    private static int EstimateResponseHeaderSize(IHttpResponseFeature? responseFeature)
    {
        const int statusLineOverhead = 32;
        const int perHeaderOverhead = 4;
        const int trailingCrlf = 2;
        const int minimumSize = 256;

        if (responseFeature?.Headers is null)
        {
            return minimumSize;
        }

        var estimate = statusLineOverhead + trailingCrlf;
        foreach (var header in responseFeature.Headers)
        {
            estimate += header.Key.Length + perHeaderOverhead;
            foreach (var v in header.Value)
            {
                estimate += v?.Length ?? 0;
            }
        }

        estimate += 128;
        return Math.Max(minimumSize, estimate);
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

        var hasUpgrade = requestHeaders.TryGetValue(WellKnownHeaders.Upgrade, out var upgradeValue)
                         && !string.IsNullOrEmpty(upgradeValue)
                         && ConnectionHeaderSemantics.HasToken(upgradeValue.ToString(), "h2c");

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
        _ops.OnOutbound(TransportData.Rent(responseBuffer));

        switchable.RequestProtocolSwitch(ops => new Http2ServerStateMachine(_h2UpgradeOptions, ops));

        return true;
    }

    public void Cleanup()
    {
        _activeResponseBodyWriter?.Dispose();
        _activeResponseBodyWriter = null;
        _serialPump?.Cleanup();
        _serialPump = null;
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _connectionCts = null;
        _pool.Dispose();
        _outboundBodyPending = false;
        _pendingResponseCount = 0;
        _activeStreamingReader = null;
        if (_requestHeadersTimerActive)
        {
            _ops.OnCancelTimer(RequestHeadersTimer);
            _requestHeadersTimerActive = false;
        }

        if (_bodyReadTimerActive)
        {
            _ops.OnCancelTimer(BodyReadTimer);
            _bodyReadTimerActive = false;
        }

        _ops.OnCancelTimer(KeepAliveTimer);
        _ops.OnCancelTimer(BodyConsumptionTimer);
        _ops.OnCancelTimer(DataRateCheck);
        _rateTimerActive = false;
    }

    private Func<IMemoryOwner<byte>, ReadOnlyMemory<byte>, ValueTask> GetSendDelegate()
    {
        return _cachedSendDelegate ??= (owner, framedData) =>
        {
            var ownerSpan = owner.Memory.Span;
            var framedSpan = framedData.Span;
            ref var ownerStart = ref MemoryMarshal.GetReference(ownerSpan);
            ref var framedStart = ref MemoryMarshal.GetReference(framedSpan);
            var offset = (int)Unsafe.ByteOffset(ref ownerStart, ref framedStart);
            _responseRate.Observe(0, framedData.Length, Now());
            EnsureRateTimer();
            var buf = TransportBuffer.Wrap(owner, offset, framedData.Length);
            _ops.OnOutbound(TransportData.Rent(buf));
            return default;
        };
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