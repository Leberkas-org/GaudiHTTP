namespace TurboHTTP.Protocol.Syntax.Http3.Options;

internal sealed record Http3ClientDecoderOptions
{
    public int MaxConcurrentStreams { get; init; } = 100;
    public int MaxFieldSectionSize { get; init; } = 64 * 1024;

    public static Http3ClientDecoderOptions Default { get; } = new();
}
