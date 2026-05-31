namespace TurboHTTP.Protocol.Syntax.Http11.Options;

internal sealed record Http11ClientEncoderOptions
{
    public bool AutoHost { get; init; } = true;
    public bool AutoAcceptEncoding { get; init; } = true;

    public static Http11ClientEncoderOptions Default { get; } = new();
}
