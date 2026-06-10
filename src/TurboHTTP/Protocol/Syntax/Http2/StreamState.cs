using System.Buffers;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Protocol.Body;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Protocol.Syntax.Http2;

/// <summary>
/// Per-stream header and body buffer management for HTTP/2.
/// Extracted from Http20ConnectionStage for independent testability.
/// </summary>
internal sealed class StreamState
{
    private IMemoryOwner<byte>? _headerOwner;
    private Memory<byte> _headerBuffer;
    private int _headerLength;
    private HttpResponseMessage? _response;
    private TurboHttpRequestFeature? _requestFeature;
    private IFeatureCollection? _features;
    private List<(string Name, string Value)>? _contentHeaders;
    private Dictionary<string, string>? _pseudoHeaders;
    private IBodyReader? _bodyReader;
    private long _maxBodySize;
    private long _totalBodyBytes;
    private Queue<StreamBodyChunk>? _outboundBuffer;

    public string BodyConsumptionTimerKey { get; private set; } = "";
    public string HeadersTimeoutTimerKey { get; private set; } = "";

    public void SetTimerKeys(int streamId)
    {
        var idStr = streamId.ToString();
        BodyConsumptionTimerKey = string.Concat("body-consumption:", idStr);
        HeadersTimeoutTimerKey = string.Concat("headers-timeout:", idStr);
    }

    public bool HasResponse => _response is not null;

    public bool HasContentHeaders => _contentHeaders is not null;

    public bool HasBodyReader => _bodyReader is not null;

    public bool HasBodyDrain { get; private set; }

    public bool HasPendingOutbound => _outboundBuffer is { Count: > 0 };

    public bool IsBodyDrainComplete { get; private set; }

    public bool IsBodyReadPending { get; set; }

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

    public void InitRequestFeature(TurboHttpRequestFeature feature)
    {
        _requestFeature = feature;
    }

    public TurboHttpRequestFeature? GetRequestFeature() => _requestFeature;

    public void SetFeatures(IFeatureCollection features)
    {
        _features = features;
    }

    public IFeatureCollection? GetFeatures() => _features;

    public void AddPseudoHeader(string name, string value)
    {
        _pseudoHeaders ??= [];
        _pseudoHeaders[name] = value;
    }

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

    public void InitBodyReader(IBodyReader reader, long maxBodySize = long.MaxValue)
    {
        _bodyReader = reader;
        _maxBodySize = maxBodySize;
        _totalBodyBytes = 0;
    }

    public void DetachBodyReader()
    {
        _bodyReader = null;
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
                streaming.Complete();
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

    public long PendingOutboundBytes { get; private set; }

    public void EnqueueBodyChunk(StreamBodyChunk chunk)
    {
        _outboundBuffer ??= new Queue<StreamBodyChunk>();
        _outboundBuffer.Enqueue(chunk);
        PendingOutboundBytes += chunk.Length;
    }

    public void PrependBodyChunk(StreamBodyChunk chunk)
    {
        _outboundBuffer ??= new Queue<StreamBodyChunk>();
        var existing = _outboundBuffer.ToArray();
        _outboundBuffer.Clear();
        _outboundBuffer.Enqueue(chunk);
        foreach (var item in existing)
        {
            _outboundBuffer.Enqueue(item);
        }

        PendingOutboundBytes += chunk.Length;
    }

    public void MarkRemoteClosed()
    {
        IsRemoteClosed = true;
    }

    public bool TryDequeueBodyChunk(out StreamBodyChunk? chunk)
    {
        if (_outboundBuffer is { Count: > 0 })
        {
            chunk = _outboundBuffer.Dequeue();
            PendingOutboundBytes -= chunk.Length;
            return true;
        }

        chunk = null;
        return false;
    }

    public StreamBodyChunk? PeekBodyChunk()
    {
        return _outboundBuffer is { Count: > 0 } ? _outboundBuffer.Peek() : null;
    }

    public void Reset()
    {
        _headerOwner?.Dispose();
        _headerOwner = null;
        _headerBuffer = default;
        _headerLength = 0;
        _response = null;
        _requestFeature = null;
        _features = null;
        _contentHeaders = null;
        _pseudoHeaders = null;
        _bodyReader?.Dispose();
        _bodyReader = null;
        HasBodyDrain = false;
        IsBodyDrainComplete = false;
        IsBodyReadPending = false;
        DisposeOutboundBuffer();
        _outboundBuffer = null;
        PendingOutboundBytes = 0;
        IsRemoteClosed = false;
        BodyConsumptionTimerKey = "";
        HeadersTimeoutTimerKey = "";
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

    private void DisposeOutboundBuffer()
    {
        if (_outboundBuffer is null)
        {
            return;
        }

        while (_outboundBuffer.Count > 0)
        {
            _outboundBuffer.Dequeue().Owner.Dispose();
        }

        PendingOutboundBytes = 0;
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