namespace TurboHTTP.Protocol.Syntax.Http2.Options;

internal sealed record Http2ClientDecoderOptions
{
    public int MaxConcurrentStreams { get; init; } = 100;
    public int InitialConnectionWindowSize { get; init; } = 64 * 1024 * 1024;
    public int InitialStreamWindowSize { get; init; } = 2 * 1024 * 1024;

    public static Http2ClientDecoderOptions Default { get; } = new();
}
