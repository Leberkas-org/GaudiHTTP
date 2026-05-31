namespace TurboHTTP.Protocol.Syntax.Http2.Options;

internal sealed record Http2ServerEncoderOptions
{
    public required int MaxFrameSize { get; init; }
    public required int HeaderTableSize { get; init; }
    public required bool WriteDateHeader { get; init; }
    public required int MaxHeaderBytes { get; init; }
}
