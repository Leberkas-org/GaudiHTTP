using TurboHTTP.Protocol.Body;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Protocol.Syntax.Http3;

/// <summary>
/// Unified per-stream state for HTTP/3 multiplexing (client and server).
/// Manages response/request assembly, pseudo-headers, content headers, body buffering,
/// and body reader handling. Pooled and reused via <see cref="Reset"/>.
/// </summary>
internal sealed class StreamState
{
    private HttpResponseMessage? _response;
    private TurboHttpRequestFeature? _requestFeature;
    private List<(string Name, string Value)>? _contentHeaders;
    private Dictionary<string, string>? _pseudoHeaders;
    private IBodyReader? _bodyReader;
    private long _maxBodySize;
    private long _totalBodyBytes;
    private Queue<StreamBodyChunk>? _outboundBuffer;

    public long StreamId { get; private set; } = -1;

    public bool HasResponse => _response is not null;

    public bool HasContentHeaders => _contentHeaders is not null;

    public bool HasBodyReader => _bodyReader is not null;

    public bool HasBodyDrain { get; private set; }

    public bool HasPendingOutbound => _outboundBuffer is { Count: > 0 };

    public bool IsBodyDrainComplete { get; private set; }

    public bool IsBodyReadPending { get; set; }

    public long PendingOutboundBytes { get; private set; }

    public long? ExpectedContentLength { get; set; }

    public string BodyConsumptionTimerKey { get; private set; } = "";
    public string HeadersTimeoutTimerKey { get; private set; } = "";
    public string DrainBodyTimerKey { get; private set; } = "";

    public void Initialize(long streamId)
    {
        StreamId = streamId;
        var idStr = streamId.ToString();
        BodyConsumptionTimerKey = string.Concat("body-consumption:", idStr);
        HeadersTimeoutTimerKey = string.Concat("headers-timeout:", idStr);
        DrainBodyTimerKey = string.Concat("drain-body:", idStr);
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

    public void InitRequestFeature(TurboHttpRequestFeature feature)
    {
        _requestFeature = feature;
    }

    public TurboHttpRequestFeature? GetRequestFeature()
    {
        return _requestFeature;
    }

    public void AddPseudoHeader(string name, string value)
    {
        _pseudoHeaders ??= [];
        _pseudoHeaders[name] = value;
    }

    public string GetPseudoHeader(string name)
    {
        if (_pseudoHeaders?.TryGetValue(name, out var value) == true)
        {
            return value;
        }

        throw new InvalidOperationException($"Pseudo-header '{name}' not found.");
    }

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

    public void EnqueueBodyChunk(StreamBodyChunk chunk)
    {
        _outboundBuffer ??= new Queue<StreamBodyChunk>();
        _outboundBuffer.Enqueue(chunk);
        PendingOutboundBytes += chunk.Length;
    }

    public StreamBodyChunk? PeekBodyChunk()
    {
        return _outboundBuffer is { Count: > 0 } ? _outboundBuffer.Peek() : null;
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

    public void Reset()
    {
        StreamId = -1;
        _response = null;
        _requestFeature = null;
        ExpectedContentLength = null;
        _contentHeaders = null;
        _pseudoHeaders = null;
        _bodyReader?.Dispose();
        _bodyReader = null;
        _maxBodySize = 0;
        _totalBodyBytes = 0;
        HasBodyDrain = false;
        IsBodyDrainComplete = false;
        IsBodyReadPending = false;
        DisposeOutboundBuffer();
        _outboundBuffer = null;
        PendingOutboundBytes = 0;
        BodyConsumptionTimerKey = "";
        HeadersTimeoutTimerKey = "";
        DrainBodyTimerKey = "";
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
}
