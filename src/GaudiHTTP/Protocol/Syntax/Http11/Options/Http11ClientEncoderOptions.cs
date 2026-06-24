namespace TurboHTTP.Protocol.Syntax.Http11.Options;

internal sealed record Http11ClientEncoderOptions
{
    public required bool AutoHost { get; init; }
    public required bool AutoAcceptEncoding { get; init; }
    public required int ChunkSize { get; init; }
}
