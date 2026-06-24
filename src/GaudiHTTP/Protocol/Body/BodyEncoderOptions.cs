namespace TurboHTTP.Protocol.Body;

internal sealed record BodyEncoderOptions
{
    public required int ChunkSize { get; init; }
}