using System.Buffers;
using System.Net;
using Akka.Actor;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.LineBased;
using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Streams.Stages.Server;
using static Servus.Senf;

namespace GaudiHTTP.Protocol.Syntax.Http11.Server;

internal sealed class Http11ServerStateMachine :
    TcpStateMachineBase<IServerStageOperations>, IServerStateMachine, IBodyDrainTarget
{
    private const string KeepAliveTimer = "keep-alive";
    private const string RequestHeadersTimer = "request-headers";
    private const string BodyConsumptionTimer = "body-consumption";
    private const string BodyReadTimer = "body-read";
    private readonly Http11ServerDecoder _decoder;
    private readonly Http11ServerEncoder _encoder;
    private readonly TimeSpan _keepAliveTimeout;
    private readonly TimeSpan _requestHeadersTimeout;

    private readonly TimeSpan _bodyConsumptionTimeout;
    private readonly TimeSpan _bodyReadTimeout;
    private readonly BodyEncoderOptions _bodyEncoderOptions;
    private readonly long _maxRequestBodySize;
    private readonly Http2ConnectionOptions _h2UpgradeOptions;
    private readonly bool _allowH2cUpgrade;

    private readonly ConnectionRateGuard _rateGuard;

    private int _pendingResponseCount;
    private bool _outboundBodyPending;
    private bool _requestHeadersTimerActive;
    private bool _bodyReadTimerActive;
    private bool _draining;
    private bool _bodyStreaming;
    private IStreamingBodyReader? _activeStreamingReader;

    private bool _isChunked;
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
        IServerStageOperations ops, TimeProvider? timeProvider = null, bool allowH2cUpgrade = true) : base(ops)
    {
        ArgumentNullException.ThrowIfNull(ops);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(h2UpgradeOptions);
        _h2UpgradeOptions = h2UpgradeOptions;
        _allowH2cUpgrade = allowH2cUpgrade;
        _bodyConsumptionTimeout = options.BodyConsumptionTimeout;
        _bodyReadTimeout = options.BodyReadTimeout;
        _bodyEncoderOptions = options.ToBodyEncoderOptions();
        _maxRequestBodySize = options.Limits.MaxRequestBodySize;
        _rateGuard = new ConnectionRateGuard(ops, options.ToRateMonitor(), timeProvider);

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

    private void EmitToTransport(ReadOnlySpan<byte> data)
    {
        var transport = Transport!;
        var mem = transport.GetMemory(data.Length);
        data.CopyTo(mem.Span);
        transport.Advance(data.Length);
        RequestFlush();
    }

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
            var transport = Transport!;

            if (_isChunked)
            {
                var framedSize = ChunkedFramingHelper.GetFramedSize(data.Length);
                var mem = transport.GetMemory(framedSize);
                ChunkedFramingHelper.WriteChunk(data.Span, mem.Span);
                transport.Advance(framedSize);
                _rateGuard.ObserveResponse(0, framedSize);
            }
            else
            {
                var mem = transport.GetMemory(data.Length);
                data.CopyTo(mem);
                transport.Advance(data.Length);
                _rateGuard.ObserveResponse(0, data.Length);
            }

            RequestFlush();

            Tracing.For("Protocol").Trace(this, "response body chunk flushed (bytes={0})", data.Length);
        }

        EmitEndStreamIfNeeded(endStream);
    }

    void IBodyDrainTarget.EmitOwnedDataFrames(int streamId, IMemoryOwner<byte> owner, int bytesWritten, bool endStream)
    {
        if (bytesWritten > 0)
        {
            var transport = Transport!;

            if (_isChunked)
            {
                var framedSize = ChunkedFramingHelper.GetFramedSize(bytesWritten);
                var mem = transport.GetMemory(framedSize);
                ChunkedFramingHelper.WriteChunk(owner.Memory.Span[..bytesWritten], mem.Span);
                transport.Advance(framedSize);
                _rateGuard.ObserveResponse(0, framedSize);
            }
            else
            {
                var mem = transport.GetMemory(bytesWritten);
                owner.Memory.Span[..bytesWritten].CopyTo(mem.Span);
                transport.Advance(bytesWritten);
                _rateGuard.ObserveResponse(0, bytesWritten);
            }

            owner.Dispose();
            RequestFlush();

            Tracing.For("Protocol").Trace(this, "response body chunk flushed (bytes={0})", bytesWritten);
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
            if (_isChunked)
            {
                EmitChunkedTerminator(_activeResponseFeatures);
            }

            var completedFeatures = _activeResponseFeatures;
            _activeResponseFeatures = null;
            Tracing.For("Protocol").Debug(this, "response body complete");
            CompleteResponse(completedFeatures);
        }
    }

    void IBodyDrainTarget.OnDrainComplete(int streamId)
    {
        Tracing.For("Protocol").Debug(this, "response body drain complete");
    }

    void IBodyDrainTarget.OnDrainFailed(int streamId, Exception reason)
    {
        // Does not route through CompleteResponse: a mid-stream drain failure never sets
        // ShouldComplete here (no other caller does either on this path), so calling the full
        // epilogue would newly rearm the keep-alive timer on a connection whose response framing
        // was left inconsistent. Reset the response bookkeeping only, preserving that behavior.
        ResetResponseState(_activeResponseFeatures);
        _activeResponseFeatures = null;

        Tracing.For("Protocol").Warning(this, "response body failed: {0}", reason.Message);
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

    /// <summary>
    /// Feeds buffered request bytes to the decoder while a request body is being streamed to the
    /// handler, advancing <paramref name="pos"/> by however much was consumed. Used both by the
    /// standalone streaming-resume preamble and by the inline body-feed right after headers are
    /// parsed — both reset the same streaming state once the decoder reports completion.
    /// </summary>
    /// <returns><see langword="true"/> if the body finished decoding on this feed.</returns>
    private bool ResumeStreamingBody(ReadOnlyMemory<byte> buffer, ref int pos)
    {
        var outcome = _decoder.Feed(buffer[pos..], out var bodyConsumed);
        pos += bodyConsumed;
        _rateGuard.ObserveRequest(0, bodyConsumed);

        if (outcome != DecodeOutcome.Complete)
        {
            return false;
        }

        _bodyStreaming = false;
        _activeStreamingReader = null;
        _rateGuard.RemoveRequest(0);
        _decoder.Reset();
        return true;
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

        try
        {
            var span = memory.Span;
            var pos = 0;

            if (_draining && _decoder.CurrentFramingDecoder is { } drainingDecoder)
            {
                var drained = drainingDecoder.Drain(span[pos..]);
                pos += drained;
                _rateGuard.ObserveRequest(0, drained);

                if (drainingDecoder.IsComplete)
                {
                    _draining = false;
                    Ops.OnCancelTimer(BodyConsumptionTimer);
                    _rateGuard.RemoveRequest(0);
                    _decoder.Reset();
                }
            }
            else if (_bodyStreaming && _decoder.StreamingReader is not null)
            {
                ResumeStreamingBody(memory, ref pos);
            }

            if (!_requestHeadersTimerActive && _pendingResponseCount == 0 && !_bodyStreaming
                && !_outboundBodyPending
                && _requestHeadersTimeout > TimeSpan.Zero)
            {
                Ops.OnScheduleTimer(RequestHeadersTimer, _requestHeadersTimeout);
                _requestHeadersTimerActive = true;
                Tracing.For("Protocol").Debug(this, "request headers timer scheduled ({0}ms)",
                    _requestHeadersTimeout.TotalMilliseconds);
            }

            while (pos < span.Length && !_bodyStreaming)
            {
                var outcome = _decoder.Feed(memory[pos..], out var consumed);
                pos += consumed;

                if (outcome == DecodeOutcome.NeedMore)
                {
                    break;
                }

                if (_requestHeadersTimerActive)
                {
                    Ops.OnCancelTimer(RequestHeadersTimer);
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

                if (!ProcessDecodedRequest(outcome))
                {
                    break;
                }

                if (outcome == DecodeOutcome.HeadersReady)
                {
                    _bodyStreaming = true;
                    Tracing.For("Protocol").Trace(this, "request body streaming started");

                    if (_decoder.StreamingReader is { } sr && _activeStreamingReader is null)
                    {
                        _activeStreamingReader = sr;
                        sr.SlotFreed += () =>
                            Ops.StageActor.Tell(new BodyResumed(), ActorRefs.NoSender);
                    }

                    if (pos < memory.Length && ResumeStreamingBody(memory, ref pos))
                    {
                        continue;
                    }

                    break;
                }

                _decoder.Reset();
            }

            ReconcileBodyReadTimer();
        }
        catch (Exception ex)
        {
            Tracing.For("Protocol").Warning(this, "Failed to decode HTTP/1.1 request (pipe): {0}", ex.Message);
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

    /// <summary>
    /// Dispatches a fully- or headers-decoded request: builds the request feature collection,
    /// applies the HTTP/1.0 keep-alive rule and the h2c Upgrade handshake, hands the request to
    /// the bridge, and triggers an Expect: 100-continue informational response if requested.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> if the request instead triggered an h2c protocol switch, signaling
    /// the caller to stop parsing further requests off this connection.
    /// </returns>
    private bool ProcessDecodedRequest(DecodeOutcome outcome)
    {
        var hasBody = outcome == DecodeOutcome.HeadersReady || _decoder.CurrentBodyReader is not null;
        var features = FeatureCollectionFactory.Create(hasBody,
            out var feature, Ops.ConnectionFeature,
            Ops.TlsHandshakeFeature, _maxRequestBodySize);
        _decoder.PopulateRequestFeature(feature);
        features.Set(new GaudiInformationalResponseFeature((statusCode, headers) =>
            SendInformational(statusCode, headers)));

        if (!ShouldComplete && feature.Protocol == WellKnownHeaders.Http10)
        {
            ShouldComplete = true;
        }

        if (_allowH2cUpgrade && TryHandleH2cUpgrade(features))
        {
            _decoder.Reset();
            return false;
        }

        _pendingResponseCount++;
        Tracing.For("Protocol").Debug(this, "request dispatched (pending={0})", _pendingResponseCount);
        Ops.OnRequest(features);

        if (string.Equals(feature.Headers[WellKnownHeaders.Expect], "100-continue",
                StringComparison.OrdinalIgnoreCase))
        {
            SendInformational(100, new HeaderDictionary());
        }

        return true;
    }

    private void ReconcileBodyReadTimer()
    {
        if (_bodyStreaming && _bodyReadTimeout > TimeSpan.Zero)
        {
            Ops.OnScheduleTimer(BodyReadTimer, _bodyReadTimeout);
            _bodyReadTimerActive = true;
        }
        else if (_bodyReadTimerActive)
        {
            Ops.OnCancelTimer(BodyReadTimer);
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

        // Single pass over the response headers computes Content-Length, explicit chunked framing, and
        // the header-buffer size estimate together, instead of three separate iterations (each a boxed
        // IHeaderDictionary enumerator) over the same dictionary.
        var headerScan = ScanResponseHeaders(responseFeature);
        var contentLength = headerScan.ContentLength;
        var hasExplicitChunked = headerScan.HasExplicitChunked;

        var isChunked = !suppressBody && (contentLength is null || hasExplicitChunked);

        // Resolve a fully-buffered response body once (the dominant Content-Length case). A non-
        // chunked buffered body is coalesced into the same pipe write as the status line + headers.
        // Streamed bodies report false here; chunked bodies keep the framed EmitBufferedBody path.
        var gaudiBody = responseBody as GaudiHttpResponseBodyFeature;
        ReadOnlyMemory<byte> bufferedBody = default;
        var hasBufferedBody = !suppressBody
            && gaudiBody is not null
            && gaudiBody.TryGetBufferedBody(out bufferedBody);
        var coalesceBody = hasBufferedBody && !isChunked;

        var transport = Transport!;
        var writer = new TransportBufferWriter(transport);
        _encoder.Encode(writer, features, isChunked, connectionClose: ShouldComplete);

        if (coalesceBody && !bufferedBody.IsEmpty)
        {
            var mem = transport.GetMemory(bufferedBody.Length);
            bufferedBody.Span.CopyTo(mem.Span);
            transport.Advance(bufferedBody.Length);
        }

        RequestFlush();

        if (suppressBody)
        {
            // Headers-only response (1xx/204/304 or HEAD): no body drain will run, so recycle the
            // feature collection now. Safe — the SM keeps no reference to `features` on this path.
            CompleteResponse(features);

            return;
        }

        ScheduleRequestBodyDrainIfUnconsumed();

        if (gaudiBody is not null)
        {
            if (coalesceBody)
            {
                // Body bytes were folded into the header buffer above: nothing more to emit.
                Tracing.For("Protocol").Debug(this,
                    "response body complete (buffered, coalesced, bytes={0})", bufferedBody.Length);
                CompleteResponse(features);

                return;
            }

            if (hasBufferedBody)
            {
                EmitBufferedBody(features, bufferedBody, isChunked);
                return;
            }

            _outboundBodyPending = true;
            _isChunked = isChunked;
            _activeResponseFeatures = features;
            Tracing.For("Protocol").Debug(this, "response body writer starting (chunked={0})", isChunked);

            var bodyStream = gaudiBody.GetResponseStream();

            _serialPump =
                new SerialBodyPump(this, EnsureConnectionCts(), _bodyEncoderOptions.ChunkSize, maxBytes: 256 * 1024);
            _serialPump.Register(bodyStream, contentLength: null, CancellationToken.None);
        }
        else
        {
            // No streamed body feature to drain: recycle the feature collection now.
            CompleteResponse(features);
        }
    }

    /// <summary>
    /// A response is being sent while the previous request's body is still unconsumed by the
    /// handler. HTTP/1.1 pipelining matches responses to requests by wire position (RFC 9112
    /// §9.3.2), so the leftover request bytes must be drained off the wire before the next
    /// request can be parsed — otherwise they would be misread as the start of the next request.
    /// </summary>
    private void ScheduleRequestBodyDrainIfUnconsumed()
    {
        if (_decoder.CurrentBodyReader is not { IsCompleted: false })
        {
            return;
        }

        if (_bodyStreaming)
        {
            _bodyStreaming = false;
            _activeStreamingReader = null;
            if (_bodyReadTimerActive)
            {
                Ops.OnCancelTimer(BodyReadTimer);
                _bodyReadTimerActive = false;
            }
        }

        _draining = true;
        Tracing.For("Protocol").Debug(this, "draining unconsumed request body");

        if (_bodyConsumptionTimeout > TimeSpan.Zero)
        {
            Ops.OnScheduleTimer(BodyConsumptionTimer, _bodyConsumptionTimeout);
        }
    }

    /// <summary>
    /// Shared tail for every response-completion path that legitimately reaches the end of a
    /// response (as opposed to <see cref="IBodyDrainTarget.OnDrainFailed"/>, which tears the
    /// connection down instead): reset per-response bookkeeping, recycle the feature collection,
    /// and rearm the keep-alive timer if the connection is otherwise idle.
    /// </summary>
    private void CompleteResponse(IFeatureCollection? features)
    {
        ResetResponseState(features);

        if (!ShouldComplete && _keepAliveTimeout > TimeSpan.Zero && _pendingResponseCount == 0)
        {
            Ops.OnScheduleTimer(KeepAliveTimer, _keepAliveTimeout);
        }
    }

    /// <summary>
    /// Clears outbound-body-pending state, drops the response rate-monitor entry (a no-op if the
    /// path never observed it — otherwise an idle keep-alive connection would be flagged as a
    /// stalled response once the grace period elapses), and recycles the feature collection.
    /// </summary>
    private void ResetResponseState(IFeatureCollection? features)
    {
        _outboundBodyPending = false;
        _rateGuard.RemoveResponse(0);
        if (features is not null)
        {
            Ops.OnResponseBodyComplete(features);
        }
    }

    private void EmitBufferedBody(IFeatureCollection features, ReadOnlyMemory<byte> body, bool isChunked)
    {
        if (body.Length > 0)
        {
            var remaining = body;
            while (remaining.Length > 0)
            {
                var take = Math.Min(remaining.Length, _bodyEncoderOptions.ChunkSize);
                var chunk = remaining[..take];

                if (isChunked)
                {
                    var framedSize = ChunkedFramingHelper.GetFramedSize(take);
                    var mem = Transport!.GetMemory(framedSize);
                    ChunkedFramingHelper.WriteChunk(chunk.Span, mem.Span);
                    Transport!.Advance(framedSize);
                    _rateGuard.ObserveResponse(0, framedSize);
                }
                else
                {
                    var mem = Transport!.GetMemory(take);
                    chunk.CopyTo(mem);
                    Transport!.Advance(take);
                    _rateGuard.ObserveResponse(0, take);
                }

                RequestFlush();

                remaining = remaining[take..];
            }
        }

        if (isChunked)
        {
            EmitChunkedTerminator(features);
        }

        Tracing.For("Protocol").Debug(this, "response body complete (buffered, bytes={0})", body.Length);
        CompleteResponse(features);
    }

    private void EmitChunkedTerminator(IFeatureCollection? features)
    {
        var trailerFeature = features?.Get<IHttpResponseTrailersFeature>();
        if (trailerFeature?.Trailers is { Count: > 0 } trailers)
        {
            if (trailers is GaudiHeaderDictionary gaudiTrailers)
            {
                gaudiTrailers.SetReadOnly();
            }

            var trailerSize = ChunkedFramingHelper.GetTrailerSectionSize(trailers);
            var totalSize = 3 + trailerSize;
            var mem = Transport!.GetMemory(totalSize);
            var written = ChunkedFramingHelper.WriteLastChunk(mem.Span);
            written += ChunkedFramingHelper.WriteTrailerSection(mem.Span[written..], trailers);
            Transport!.Advance(written);
            RequestFlush();

            Tracing.For("Protocol").Debug(this, "response trailers emitted ({0} bytes)", trailerSize);
        }
        else
        {
            var mem = Transport!.GetMemory(5);
            ChunkedFramingHelper.WriteTerminator(mem.Span);
            Transport!.Advance(5);
            RequestFlush();
        }
    }

    private void SendInformational(int statusCode, IHeaderDictionary headers)
    {
        var estimatedSize = 64;
        foreach (var h in headers)
        {
            estimatedSize += h.Key.Length + 4;
            foreach (var v in h.Value)
            {
                estimatedSize += (v?.Length ?? 0) + 2;
            }
        }

        var mem = Transport!.GetMemory(estimatedSize);
        var writer = SpanWriter.Create(mem.Span);
        StatusLineWriter.Write(ref writer, HttpVersion.Version11, statusCode);

        var headerCollection = new HeaderCollection();
        foreach (var h in headers)
        {
            foreach (var v in h.Value)
            {
                if (v is not null)
                {
                    headerCollection.Add(h.Key, v);
                }
            }
        }

        HeaderBlockWriter.Write(ref writer, headerCollection);
        Transport!.Advance(writer.BytesWritten);
        RequestFlush();
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
        else if (name == ConnectionRateGuard.TimerName)
        {
            if (_rateGuard.OnTimerFired((req, resp) =>
                    Tracing.For("Protocol").Warning(this,
                        "data rate violation (reqRate={0}, respRate={1}, paused={2})",
                        req, resp, ShouldPauseNetwork)))
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

    internal readonly struct ResponseHeaderScan(long? contentLength, bool hasExplicitChunked, int estimatedSize)
    {
        public long? ContentLength { get; } = contentLength;
        public bool HasExplicitChunked { get; } = hasExplicitChunked;
        public int EstimatedSize { get; } = estimatedSize;
    }

    /// <summary>
    /// Single pass over the response headers computing the Content-Length, whether Transfer-Encoding
    /// declares chunked, and the header-buffer size estimate together — replacing three separate
    /// iterations (each a boxed IHeaderDictionary enumerator) over the same dictionary per response.
    /// </summary>
    internal static ResponseHeaderScan ScanResponseHeaders(IHttpResponseFeature? responseFeature)
    {
        const int statusLineOverhead = 32;
        const int perHeaderOverhead = 4;
        const int trailingCrlf = 2;
        const int slack = 128;
        const int minimumSize = 256;

        if (responseFeature?.Headers is not { } headers)
        {
            return new ResponseHeaderScan(null, false, minimumSize);
        }

        long? contentLength = null;
        var hasExplicitChunked = false;
        var estimate = statusLineOverhead + trailingCrlf;

        foreach (var header in headers)
        {
            estimate += header.Key.Length + perHeaderOverhead;
            foreach (var v in header.Value)
            {
                estimate += v?.Length ?? 0;
            }

            if (contentLength is null
                && header.Key.Equals(WellKnownHeaders.ContentLength, StringComparison.OrdinalIgnoreCase)
                && header.Value.Count > 0
                && header.Value[0] is { } clValue
                && ContentLengthSemantics.TryParse(clValue, out var parsed))
            {
                contentLength = parsed;
            }
            else if (!hasExplicitChunked
                && header.Key.Equals(WellKnownHeaders.TransferEncoding, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var v in header.Value)
                {
                    if (v != null && v.Equals(WellKnownHeaders.ChunkedValue, StringComparison.OrdinalIgnoreCase))
                    {
                        hasExplicitChunked = true;
                        break;
                    }
                }
            }
        }

        estimate += slack;
        return new ResponseHeaderScan(contentLength, hasExplicitChunked, Math.Max(minimumSize, estimate));
    }

    private bool TryHandleH2cUpgrade(IFeatureCollection features)
    {
        if (Ops is not IProtocolSwitchCapable switchable)
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
        EmitToTransport(responseBytes);

        switchable.RequestProtocolSwitch(ops => new Http2ServerStateMachine(_h2UpgradeOptions, ops));

        return true;
    }

    public void ResumeBody()
    {
        RequestRead();
    }

    public void Cleanup()
    {
        _serialPump?.Cleanup();
        _serialPump = null;
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _connectionCts = null;
        _outboundBodyPending = false;
        _pendingResponseCount = 0;
        _activeStreamingReader = null;
        if (_requestHeadersTimerActive)
        {
            Ops.OnCancelTimer(RequestHeadersTimer);
            _requestHeadersTimerActive = false;
        }

        if (_bodyReadTimerActive)
        {
            Ops.OnCancelTimer(BodyReadTimer);
            _bodyReadTimerActive = false;
        }

        Ops.OnCancelTimer(KeepAliveTimer);
        Ops.OnCancelTimer(BodyConsumptionTimer);
        CleanupTransportIo();
        _rateGuard.Cleanup();
    }
}