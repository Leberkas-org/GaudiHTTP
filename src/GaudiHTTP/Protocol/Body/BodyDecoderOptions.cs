namespace GaudiHTTP.Protocol.Body;

internal sealed record BodyDecoderOptions
{
    public required long StreamingThreshold { get; init; }
    public required long MaxBufferedBodySize { get; init; }
    public required long? MaxStreamedBodySize { get; init; }
    public required int MaxChunkExtensionLength { get; init; }
}