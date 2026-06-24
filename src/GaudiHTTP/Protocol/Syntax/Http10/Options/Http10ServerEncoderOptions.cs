namespace TurboHTTP.Protocol.Syntax.Http10.Options;

internal sealed record Http10ServerEncoderOptions
{
    public required bool WriteDateHeader { get; init; }
    public required int MaxHeaderBytes { get; init; }
}
