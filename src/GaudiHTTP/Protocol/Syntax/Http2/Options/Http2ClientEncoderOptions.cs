namespace TurboHTTP.Protocol.Syntax.Http2.Options;

internal sealed record Http2ClientEncoderOptions
{
    public required int HeaderTableSize { get; init; }
    public required int MaxFrameSize { get; init; }
}
