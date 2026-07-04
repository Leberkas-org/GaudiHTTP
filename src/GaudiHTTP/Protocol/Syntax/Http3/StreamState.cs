using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Pooling;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Protocol;

namespace GaudiHTTP.Protocol.Syntax.Http3;

/// <summary>
/// Unified per-stream state for HTTP/3 multiplexing (client and server).
/// Manages response/request assembly, pseudo-headers, content headers, body buffering,
/// and body reader handling. Pooled and reused via <see cref="Reset"/>.
/// </summary>
internal sealed class StreamState : Poolable<StreamState>
{
    private HttpResponseMessage? _response;
    private GaudiHttpRequestFeature? _requestFeature;
    private List<(string Name, string Value)>? _contentHeaders;
    private string? _pseudoMethod;
    private string? _pseudoPath;
    private string? _pseudoScheme;
    private string? _pseudoAuthority;
    private IBodyReader? _bodyReader;
    private long _maxBodySize;
    private long _totalBodyBytes;
    private List<byte[]>? _pendingInboundData;
    private IFeatureCollection? _features;

    public long StreamId { get; private set; } = -1;

    public bool HasResponse => _response is not null;

    public bool HasContentHeaders => _contentHeaders is { Count: > 0 };

    public bool HasBodyReader => _bodyReader is not null;

    public bool HasBodyDrain { get; private set; }

    public bool IsBodyDrainComplete { get; private set; }

    public bool IsBodyReadPending { get; set; }

    /// <summary>
    /// RFC 9204 §2.1.2 — true while inbound HEADERS are QPACK-blocked awaiting dynamic-table
    /// updates. DATA frames received in this window are buffered (not dropped) and replayed
    /// once the stream resolves; a QUIC FIN is remembered via <see cref="PendingEndStream"/>.
    /// </summary>
    public bool IsHeadersBlocked { get; set; }

    /// <summary>A QUIC FIN arrived while the stream was still QPACK-blocked.</summary>
    public bool PendingEndStream { get; set; }

    public long? ExpectedContentLength { get; set; }

    public string BodyConsumptionTimerKey { get; private set; } = "";
    public string HeadersTimeoutTimerKey { get; private set; } = "";

    public void Initialize(long streamId)
    {
        if (StreamId == streamId)
        {
            return;
        }

        StreamId = streamId;
        var idStr = streamId.ToString();
        BodyConsumptionTimerKey = string.Concat("body-consumption:", idStr);
        HeadersTimeoutTimerKey = string.Concat("headers-timeout:", idStr);
    }

    public HttpResponseMessage InitResponse()
    {
        _response = new HttpResponseMessage();
        return _response;
    }

    public HttpResponseMessage GetResponse()
    {
        return _response ?? throw new InvalidOperationException("No response has been initialized.");
    }

    public void InitRequestFeature(GaudiHttpRequestFeature feature)
    {
        _requestFeature = feature;
    }

    public GaudiHttpRequestFeature? GetRequestFeature()
    {
        return _requestFeature;
    }

    public string? PseudoMethod { get => _pseudoMethod; set => _pseudoMethod = value; }
    public string? PseudoPath { get => _pseudoPath; set => _pseudoPath = value; }
    public string? PseudoScheme { get => _pseudoScheme; set => _pseudoScheme = value; }
    public string? PseudoAuthority { get => _pseudoAuthority; set => _pseudoAuthority = value; }

    public void AddContentHeader(string name, string value)
    {
        _contentHeaders ??= [];
        _contentHeaders.Add((name, value));
    }

    public IReadOnlyList<(string Name, string Value)>? ContentHeaders => _contentHeaders;

    public void ApplyContentHeadersTo(HttpContent content)
    {
        if (_contentHeaders is null)
        {
            return;
        }

        foreach (var (name, value) in _contentHeaders)
        {
            content.Headers.TryAddWithoutValidation(name, value);
        }
    }

    /// <summary>
    /// Peeks the declared Content-Length from the accumulated content headers without
    /// materializing an <see cref="HttpContent"/>. Used to decide buffered-vs-streaming body
    /// handling before a body reader is created (the reader must exist before DATA frames arrive).
    /// </summary>
    public long? PeekContentLength()
    {
        if (_contentHeaders is null)
        {
            return null;
        }

        foreach (var (name, value) in _contentHeaders)
        {
            if (string.Equals(name, WellKnownHeaders.ContentLength, StringComparison.OrdinalIgnoreCase)
                && long.TryParse(value, out var length))
            {
                return length;
            }
        }

        return null;
    }

    public void InitBodyReader(IBodyReader reader, long maxBodySize = long.MaxValue)
    {
        _bodyReader = reader;
        _maxBodySize = maxBodySize;
        _totalBodyBytes = 0;
        ExpectedContentLength = PeekContentLength();
    }

    public void DetachBodyReader()
    {
        _bodyReader = null;
    }

    public IBodyReader? TakeBodyReader()
    {
        var reader = _bodyReader;
        _bodyReader = null;
        return reader;
    }

    /// <summary>
    /// Detaches and returns the body reader only if it is a still-pending <see cref="BufferedBodyReader"/>
    /// (dispatch deferred until the body completed). Non-buffered readers (e.g. <c>QueuedBodyReader</c>)
    /// are left attached — their lifecycle is owned by the already-handed-out <see cref="Stream"/>, and
    /// detaching them here would prevent the owning session manager from returning them to the pool later.
    /// </summary>
    public bool TryTakeBufferedBodyReader(out BufferedBodyReader? reader)
    {
        if (_bodyReader is BufferedBodyReader buffered)
        {
            reader = buffered;
            _bodyReader = null;
            return true;
        }

        reader = null;
        return false;
    }

    public void FeedBody(ReadOnlySpan<byte> data, bool endStream)
    {
        if (!data.IsEmpty)
        {
            _totalBodyBytes += data.Length;
            if (_totalBodyBytes > _maxBodySize)
            {
                throw new HttpProtocolException(
                    string.Concat("Request body size ", _totalBodyBytes.ToString(), " exceeds limit ", _maxBodySize.ToString(), "."));
            }
        }

        if (_bodyReader is IBufferedBodyReader buffered)
        {
            if (!data.IsEmpty)
            {
                buffered.Feed(data);
            }

            if (endStream)
            {
                if (ExpectedContentLength is { } expected && _totalBodyBytes != expected)
                {
                    throw new HttpProtocolException(
                        string.Concat("Buffered body ended after ", _totalBodyBytes.ToString(),
                            " bytes but Content-Length declared ", expected.ToString(), "."));
                }

                buffered.MarkComplete();
            }

            return;
        }

        if (_bodyReader is IStreamingBodyReader streaming)
        {
            if (!data.IsEmpty)
            {
                streaming.TryEnqueue(data);
            }

            if (endStream)
            {
                if (ExpectedContentLength is { } expected && _totalBodyBytes != expected)
                {
                    streaming.Fault(new HttpRequestException(
                        $"Response body ended after {_totalBodyBytes} bytes but Content-Length declared {expected}."));
                }
                else
                {
                    streaming.Complete();
                }
            }
        }
    }

    /// <summary>
    /// Buffers a copy of inbound DATA received while the stream is QPACK-blocked. The frame
    /// aliases a pooled transport buffer that the caller reuses after handling, so the bytes
    /// must be copied to survive until <see cref="ReplayPendingInboundData"/> runs.
    /// </summary>
    public void BufferInboundData(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        _pendingInboundData ??= [];
        _pendingInboundData.Add(data.ToArray());
    }

    /// <summary>Feeds all DATA buffered while QPACK-blocked into the now-initialized body reader.</summary>
    public void ReplayPendingInboundData()
    {
        if (_pendingInboundData is null)
        {
            return;
        }

        foreach (var chunk in _pendingInboundData)
        {
            FeedBody(chunk, endStream: false);
        }

        _pendingInboundData = null;
    }

    public Stream GetBodyStream()
    {
        if (_bodyReader is null)
        {
            throw new InvalidOperationException("No body reader has been initialized.");
        }

        return _bodyReader.AsStream();
    }

    public void AbortBody()
    {
        if (_bodyReader is IStreamingBodyReader streaming)
        {
            streaming.Fault(new OperationCanceledException());
        }

        _bodyReader?.Dispose();
    }

    public void MarkBodyDrainActive()
    {
        HasBodyDrain = true;
        IsBodyDrainComplete = false;
    }

    public void MarkBodyDrainComplete()
    {
        IsBodyDrainComplete = true;
    }

    public void SetFeatures(IFeatureCollection features)
    {
        _features = features;
    }

    public IFeatureCollection? GetFeatures() => _features;

    protected override void OnReset()
    {
        StreamId = -1;
        _response = null;
        _requestFeature = null;
        ExpectedContentLength = null;
        _contentHeaders?.Clear();
        _pseudoMethod = null;
        _pseudoPath = null;
        _pseudoScheme = null;
        _pseudoAuthority = null;
        _bodyReader?.Dispose();
        _bodyReader = null;
        _maxBodySize = 0;
        _totalBodyBytes = 0;
        HasBodyDrain = false;
        IsBodyDrainComplete = false;
        IsBodyReadPending = false;
        IsHeadersBlocked = false;
        PendingEndStream = false;
        _pendingInboundData = null;
        _features = null;
        // Timer keys intentionally NOT cleared — they are stream-ID-derived strings that survive
        // pool reuse. Initialize() overwrites them for the next stream ID, avoiding a redundant
        // allocation + re-allocation cycle on every pool return/checkout.
    }
}
