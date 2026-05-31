namespace TurboHTTP.Protocol.Syntax.Http2.Options;

internal sealed record Http2ServerDecoderOptions
{
    public required int HeaderTableSize { get; init; }
    public required int MaxConcurrentStreams { get; init; }
    public required int MaxFieldSectionSize { get; init; }
    public required int MaxHeaderBytes { get; init; }
    public required int MaxHeaderCount { get; init; }
}
