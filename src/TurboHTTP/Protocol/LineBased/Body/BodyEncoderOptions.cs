namespace TurboHTTP.Protocol.LineBased.Body;

/// <summary>
/// Configuration for line-based (HTTP/1.x) body encoders built by <see cref="BodyEncoderFactory"/>.
/// </summary>
internal sealed record BodyEncoderOptions
{
    public int ChunkSize { get; init; } = 16 * 1024;

    public static BodyEncoderOptions Default { get; } = new();
}
