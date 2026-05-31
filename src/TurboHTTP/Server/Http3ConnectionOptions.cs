namespace TurboHTTP.Server;

internal sealed record Http3ConnectionOptions
{
    public required ResolvedServerLimits Limits { get; init; }

    public required int MaxConcurrentStreams { get; init; }
    public required int MaxHeaderListSize { get; init; }
    public required int MaxHeaderCount { get; init; }
    public required int QpackMaxTableCapacity { get; init; }
    public required int QpackBlockedStreams { get; init; }

    public required int BodyBufferThreshold { get; init; }
    public required int ResponseBodyChunkSize { get; init; }
    public required TimeSpan BodyConsumptionTimeout { get; init; }
}
