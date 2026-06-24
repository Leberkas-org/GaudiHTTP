namespace GaudiHTTP.Protocol.Syntax.Http11.Options;

internal sealed record Http11ServerEncoderOptions
{
    public required TimeSpan KeepAliveTimeout { get; init; }
    public required TimeSpan RequestHeadersTimeout { get; init; }
    public required bool WriteDateHeader { get; init; }
    public required int MaxHeaderBytes { get; init; }
}
