namespace TurboHTTP.Protocol.LineBased.Body;

/// <summary>
/// Size and framing limits that drive how <see cref="BodyDecoderFactory"/> builds a line-based
/// (HTTP/1.x) request/response body decoder. Bundles what used to be a handful of loose primitive
/// factory parameters so client and server pass a single options object.
/// </summary>
internal sealed record BodyDecoderOptions
{
    public required long StreamingThreshold { get; init; }
    public required long MaxBufferedBodySize { get; init; }
    public required long? MaxStreamedBodySize { get; init; }
    public required int MaxChunkExtensionLength { get; init; }
}
