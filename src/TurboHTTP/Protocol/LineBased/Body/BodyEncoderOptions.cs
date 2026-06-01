namespace TurboHTTP.Protocol.LineBased.Body;

/// <summary>
/// Configuration for line-based (HTTP/1.x) body encoders built by <see cref="BodyEncoderFactory"/>.
/// </summary>
internal sealed record BodyEncoderOptions
{
    public required int ChunkSize { get; init; }
}
