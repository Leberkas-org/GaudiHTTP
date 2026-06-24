using GaudiHTTP.Pooling;

namespace GaudiHTTP.Protocol.Body;

internal sealed class ContentLengthFramingDecoder : IFramingDecoder, IResettable
{
    private long _remaining;

    public bool SupportsZeroCopy => true;
    public bool IsComplete => _remaining == 0;
    public IReadOnlyList<(string Name, string Value)> Trailers => [];

    public void Reset(long contentLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(contentLength);
        _remaining = contentLength;
    }

    void IResettable.Reset() => Reset(0);

    public FramingDecodeResult Decode(ReadOnlySpan<byte> raw, out int rawConsumed)
    {
        var take = (int)Math.Min(_remaining, raw.Length);
        rawConsumed = take;
        _remaining -= take;
        return new FramingDecodeResult(raw[..take], _remaining == 0);
    }

    public bool OnEof() => _remaining == 0;

    public int Drain(ReadOnlySpan<byte> raw)
    {
        var take = (int)Math.Min(_remaining, raw.Length);
        _remaining -= take;
        return take;
    }
}
