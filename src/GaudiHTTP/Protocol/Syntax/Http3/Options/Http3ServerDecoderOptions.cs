namespace GaudiHTTP.Protocol.Syntax.Http3.Options;

internal sealed record Http3ServerDecoderOptions
{
    public required int MaxConcurrentStreams { get; init; }
    public required int MaxFieldSectionSize { get; init; }
    public required int MaxHeaderBytes { get; init; }
    public required int MaxHeaderCount { get; init; }
}
