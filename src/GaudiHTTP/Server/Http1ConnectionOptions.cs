namespace GaudiHTTP.Server;

internal sealed record Http1ConnectionOptions
{
    public required ResolvedServerLimits Limits { get; init; }

    public required int MaxRequestLineLength { get; init; }
    public required int MaxRequestTargetLength { get; init; }
    public required int MaxPipelinedRequests { get; init; }
    public required int MaxChunkExtensionLength { get; init; }
    public required int MaxChunkedControlLineLength { get; init; }
    public required int MaxChunkedTrailerSize { get; init; }
    public required int MaxHeaderListSize { get; init; }
    public required int MaxHeaderCount { get; init; }
    public required bool AllowObsFold { get; init; }
    public required TimeSpan BodyReadTimeout { get; init; }
    public required int MaxBufferedBodySize { get; init; }
    public required int ResponseBodyChunkSize { get; init; }
    public required TimeSpan BodyConsumptionTimeout { get; init; }
}