namespace GaudiHTTP.Protocol.Syntax.Http3.Options;

internal sealed record Http3ClientDecoderOptions
{
    public required int MaxConcurrentStreams { get; init; }
    public required int MaxFieldSectionSize { get; init; }
}
