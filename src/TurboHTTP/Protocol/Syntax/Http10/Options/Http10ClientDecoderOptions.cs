namespace TurboHTTP.Protocol.Syntax.Http10.Options;

internal sealed record Http10ClientDecoderOptions
{
    public long StreamingThreshold { get; init; } = 64 * 1024;
    public long MaxBufferedBodySize { get; init; } = 4 * 1024 * 1024;
    public long? MaxStreamedBodySize { get; init; }
    public int MaxHeaderBytes { get; init; } = 32 * 1024;
    public int MaxHeaderCount { get; init; } = 100;
    public int HeaderLineMaxLength { get; init; } = 8 * 1024;
    public bool AllowObsFold { get; init; }

    public static Http10ClientDecoderOptions Default { get; } = new();
}
