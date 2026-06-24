namespace GaudiHTTP.Protocol.Body;

internal readonly ref struct FramingDecodeResult(ReadOnlySpan<byte> body, bool endOfBody)
{
    public ReadOnlySpan<byte> Body { get; } = body;
    public bool EndOfBody { get; } = endOfBody;
}

internal interface IFramingDecoder
{
    bool SupportsZeroCopy { get; }
    bool IsComplete { get; }
    FramingDecodeResult Decode(ReadOnlySpan<byte> raw, out int rawConsumed);
    bool OnEof();
    int Drain(ReadOnlySpan<byte> raw);
    IReadOnlyList<(string Name, string Value)> Trailers { get; }
}
