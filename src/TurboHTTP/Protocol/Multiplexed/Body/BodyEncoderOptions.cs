namespace TurboHTTP.Protocol.Multiplexed.Body;

/// <summary>
/// Configuration for multiplexed (HTTP/2 and HTTP/3) body encoders built by
/// <see cref="BodyEncoderFactory"/>.
/// </summary>
internal sealed record BodyEncoderOptions
{
    public required int ChunkSize { get; init; }
}
