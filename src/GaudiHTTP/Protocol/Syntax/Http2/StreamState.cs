using System.Buffers;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Pooling;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Protocol;

namespace GaudiHTTP.Protocol.Syntax.Http2;

/// <summary>
/// Per-stream header and body buffer management for HTTP/2.
/// Extracted from Http20ConnectionStage for independent testability.
/// </summary>
internal sealed class StreamState : Poolable<StreamState>
{
    private IMemoryOwner<byte>? _headerOwner;
    private Memory<byte> _headerBuffer;
    private int _headerLength;
    private HttpResponseMessage? _response;
    private GaudiHttpRequestFeature? _requestFeature;
    private IFeatureCollection? _features;
    private List<(string Name, string Value)>? _contentHeaders;
    private string? _pseudoMethod;
    private string? _pseudoPath;
    private string? _pseudoScheme;
    private string? _pseudoAuthority;
    private IBodyReader? _bodyReader;
    private long _maxBodySize;
    private long _totalBodyBytes;
    private ReadOnlyMemory<byte> _bufferedRemainder;

    // Unsent slice of a buffered response body that did not fit the H2 send window. Held with no
    // copy (it points into the response feature's still-live WrittenMemory) and emitted directly on
    // WINDOW_UPDATE — see Http2ServerSessionManager.DrainBufferedRemainder.
    public bool HasBufferedRemainder => !_bufferedRemainder.IsEmpty;
    public ReadOnlyMemory<byte> BufferedRemainder => _bufferedRemainder;
    public void SetBufferedRemainder(ReadOnlyMemory<byte> remainder) => _bufferedRemainder = remainder;
    public void AdvanceBufferedRemainder(int count) => _bufferedRemainder = _bufferedRemainder[count..];

    public string BodyConsumptionTimerKey { get; private set; } = "";
    public string HeadersTimeoutTimerKey { get; private set; } = "";

    public void SetTimerKeys(int streamId)
    {
        var idStr = streamId.ToString();
        BodyConsumptionTimerKey = string.Concat("body-consumption:", idStr);
        HeadersTimeoutTimerKey = string.Concat("headers-timeout:", idStr);
    }

    public bool HasResponse => _response is not null;

    public bool HasContentHeaders => _contentHeaders is { Count: > 0 };

    public bool HasBodyReader => _bodyReader is not null;

    public bool HasBodyDrain { get; private set; }

    public bool IsBodyDrainComplete { get; private set; }

    public bool IsBodyReadPending { get; set; }

    /// <summary>
    /// Declared Content-Length of the body being fed, when known. When set, an END_STREAM
    /// arriving before (or after) exactly this many bytes faults the body reader instead of
    /// completing it, so a truncated body surfaces as an error rather than silent success.
    /// </summary>
    public long? ExpectedBodyLength { get; private set; }

    public bool IsRemoteClosed { get; private set; }

    public ReadOnlySpan<byte> GetHeaderSpan()
    {
        return _headerBuffer[.._headerLength].Span;
    }

    public void ClearHeaderBuffer()
    {
        _headerLength = 0;
    }

    public void InitResponse(HttpResponseMessage response)
    {
        _response = response;
    }

    public HttpResponseMessage GetOrCreateResponse()
    {
        return _response ??= new HttpResponseMessage();
    }

    public HttpResponseMessage GetResponse()
    {
        return _response ?? throw new InvalidOperationException("No response has been initialized.");
    }

    public void InitRequestFeature(GaudiHttpRequestFeature feature)
    {
        _requestFeature = feature;
    }

    public GaudiHttpRequestFeature? GetRequestFeature() => _requestFeature;

    public void SetFeatures(IFeatureCollection features)
    {
        _features = features;
    }

    public IFeatureCollection? GetFeatures() => _features;

    public string? PseudoMethod { get => _pseudoMethod; set => _pseudoMethod = value; }
    public string? PseudoPath { get => _pseudoPath; set => _pseudoPath = value; }
    public string? PseudoScheme { get => _pseudoScheme; set => _pseudoScheme = value; }
    public string? PseudoAuthority { get => _pseudoAuthority; set => _pseudoAuthority = value; }

    public void AddContentHeader(string name, string value)
    {
        _contentHeaders ??= [];
        _contentHeaders.Add((name, value));
    }

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
        ExpectedBodyLength = PeekContentLength();
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
                    string.Concat("Request body size ", _totalBodyBytes.ToString(), " exceeds limit ",
                        _maxBodySize.ToString(), "."));
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
                if (ExpectedBodyLength is { } expected && _totalBodyBytes != expected)
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
                if (ExpectedBodyLength is { } expected && _totalBodyBytes != expected)
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

    public void MarkRemoteClosed()
    {
        IsRemoteClosed = true;
    }

    protected override void OnReset()
    {
        _headerOwner?.Dispose();
        _headerOwner = null;
        _headerBuffer = default;
        _headerLength = 0;
        _response = null;
        _requestFeature = null;
        _features = null;
        _contentHeaders?.Clear();
        _pseudoMethod = null;
        _pseudoPath = null;
        _pseudoScheme = null;
        _pseudoAuthority = null;
        _bodyReader?.Dispose();
        _bodyReader = null;
        _bufferedRemainder = default;
        HasBodyDrain = false;
        IsBodyDrainComplete = false;
        IsBodyReadPending = false;
        ExpectedBodyLength = null;
        IsRemoteClosed = false;
        // Timer keys intentionally NOT cleared — they are stream-ID-derived strings that survive
        // pool reuse. SetTimerKeys() overwrites them for the next stream ID, avoiding a redundant
        // allocation + re-allocation cycle on every pool return/checkout.
    }

    public void AppendHeader(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        EnsureHeaderCapacity(_headerLength + data.Length);
        data.CopyTo(_headerBuffer.Span[_headerLength..]);
        _headerLength += data.Length;
    }

    /// <summary>
    /// Appends a header-block fragment, rejecting the stream's accumulated (still-compressed) header
    /// block once it exceeds <paramref name="maxAccumulatedBytes"/>. RFC 9113 §6.10 / CVE-2024-27316:
    /// bounds a HEADERS+CONTINUATION flood before the block is buffered and HPACK-decoded. Using the
    /// decoded-size limit as the compressed-size ceiling is conservative - HPACK never expands below
    /// the compressed input for valid traffic, so legitimate requests are unaffected.
    /// </summary>
    public void AppendHeader(ReadOnlySpan<byte> data, int maxAccumulatedBytes)
    {
        if (data.IsEmpty)
        {
            return;
        }

        if ((long)_headerLength + data.Length > maxAccumulatedBytes)
        {
            throw new HttpProtocolException(
                $"RFC 9113 §6.10: accumulated header block exceeds the maximum of {maxAccumulatedBytes} bytes.");
        }

        AppendHeader(data);
    }

    private void EnsureHeaderCapacity(int required)
    {
        if (_headerOwner == null || required > _headerBuffer.Length)
        {
            RentNewHeaderBuffer(required);
        }
    }

    private void RentNewHeaderBuffer(int size)
    {
        var newOwner = MemoryPool<byte>.Shared.Rent(size);
        if (_headerOwner != null)
        {
            _headerBuffer.Span.CopyTo(newOwner.Memory.Span);
            _headerOwner.Dispose();
        }

        _headerOwner = newOwner;
        _headerBuffer = newOwner.Memory;
    }
}