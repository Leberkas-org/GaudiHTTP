namespace GaudiHTTP.Protocol.Syntax.Http10.Options;

internal sealed record Http10ServerDecoderOptions
{
    public required long StreamingThreshold { get; init; }
    public required long MaxBufferedBodySize { get; init; }
    public required long? MaxStreamedBodySize { get; init; }
    public required int MaxHeaderBytes { get; init; }
    public required int MaxHeaderCount { get; init; }
    public required int HeaderLineMaxLength { get; init; }
    public required int RequestLineMaxLength { get; init; }
    public required int MaxRequestTargetLength { get; init; }
    public required int MaxChunkedControlLineLength { get; init; }
    public required int MaxChunkedTrailerSize { get; init; }
    public required bool AllowObsFold { get; init; }
}
