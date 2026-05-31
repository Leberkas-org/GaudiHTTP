namespace TurboHTTP.Protocol.Syntax.Http2.Options;

internal sealed record Http2ClientEncoderOptions
{
    public int HeaderTableSize { get; init; } = 64 * 1024;
    public int MaxFrameSize { get; init; } = 16 * 1024;

    public static Http2ClientEncoderOptions Default { get; } = new();
}
