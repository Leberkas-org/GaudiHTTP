namespace TurboHTTP.Protocol.Body;

internal readonly struct BodyReaderClassification
{
    public bool HasBody { get; init; }
    public bool IsBuffered { get; init; }
    public bool IsChunked { get; init; }
    public bool HasContentLength { get; init; }
    public long ContentLength { get; init; }
}
