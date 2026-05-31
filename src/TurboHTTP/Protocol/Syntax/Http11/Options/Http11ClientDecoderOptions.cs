namespace TurboHTTP.Protocol.Syntax.Http11.Options;

internal sealed record Http11ClientDecoderOptions
{
    public long StreamingThreshold { get; init; } = 64 * 1024;
    public long MaxBufferedBodySize { get; init; } = 4 * 1024 * 1024;
    public long? MaxStreamedBodySize { get; init; }
    public int MaxHeaderBytes { get; init; } = 32 * 1024;
    public int MaxHeaderCount { get; init; } = 100;
    public int HeaderLineMaxLength { get; init; } = 8 * 1024;
    public bool AllowObsFold { get; init; }
    public int MaxPipelineDepth { get; init; } = 1;

    public static Http11ClientDecoderOptions Default { get; } = new();
}
