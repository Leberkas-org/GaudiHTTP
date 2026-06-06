namespace TurboHTTP.Protocol.Body;

internal sealed class CloseDelimitedFramingDecoder : IFramingDecoder
{
    private long _totalBytes;
    private long _maxBodySize;

    public bool SupportsZeroCopy => true;
    public bool IsComplete { get; private set; }
    public IReadOnlyList<(string Name, string Value)> Trailers => [];

    public void Reset(long maxBodySize)
    {
        _totalBytes = 0;
        _maxBodySize = maxBodySize;
        IsComplete = false;
    }

    public FramingDecodeResult Decode(ReadOnlySpan<byte> raw, out int rawConsumed)
    {
        _totalBytes += raw.Length;
        if (_totalBytes > _maxBodySize)
        {
            throw new HttpProtocolException($"Request body size {_totalBytes} exceeds limit {_maxBodySize}.");
        }

        rawConsumed = raw.Length;
        return new FramingDecodeResult(raw, false);
    }

    public bool OnEof()
    {
        IsComplete = true;
        return true;
    }

    public int Drain(ReadOnlySpan<byte> raw) => raw.Length;
}
