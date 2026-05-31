namespace TurboHTTP.Protocol.LineBased.Body;

/// <summary>
/// Size and framing limits that drive how <see cref="BodyDecoderFactory"/> builds a line-based
/// (HTTP/1.x) request/response body decoder. Bundles what used to be a handful of loose primitive
/// factory parameters so client and server pass a single options object.
/// </summary>
internal sealed record BodyDecoderOptions
{
    public long StreamingThreshold { get; init; } = 64 * 1024;
    public long MaxBufferedBodySize { get; init; } = 4 * 1024 * 1024;
    public long? MaxStreamedBodySize { get; init; }
    public long MaxBodySize { get; init; } = 10 * 1024 * 1024;
    public int MaxChunkExtensionLength { get; init; } = int.MaxValue;

    public static BodyDecoderOptions Default { get; } = new();
}
