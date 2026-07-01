namespace GaudiHTTP.Protocol.Syntax.Http11.Options;

internal sealed record Http11ClientDecoderOptions
{
    public required long StreamingThreshold { get; init; }
    public required long MaxBufferedBodySize { get; init; }
    public required long? MaxStreamedBodySize { get; init; }
    public required int MaxHeaderBytes { get; init; }
    public required int MaxHeaderCount { get; init; }
    public required int HeaderLineMaxLength { get; init; }
    public required int MaxChunkExtensionLength { get; init; }
    public required int MaxChunkedControlLineLength { get; init; }
    public required int MaxChunkedTrailerSize { get; init; }
    public required bool AllowObsFold { get; init; }
}
