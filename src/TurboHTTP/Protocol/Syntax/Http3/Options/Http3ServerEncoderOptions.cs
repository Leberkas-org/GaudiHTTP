namespace TurboHTTP.Protocol.Syntax.Http3.Options;

internal sealed record Http3ServerEncoderOptions
{
    public required bool WriteDateHeader { get; init; }
    public required int QpackMaxTableCapacity { get; init; }
    public required int QpackBlockedStreams { get; init; }
    public required int MaxHeaderBytes { get; init; }
    public required bool UseHuffman { get; init; }
}
