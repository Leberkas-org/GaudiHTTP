namespace TurboHTTP.Server;

internal sealed record Http2ConnectionOptions
{
    public required ResolvedServerLimits Limits { get; init; }

    public required int MaxConcurrentStreams { get; init; }
    public required int InitialConnectionWindowSize { get; init; }
    public required int InitialStreamWindowSize { get; init; }
    public required int MaxFrameSize { get; init; }
    public required int HeaderTableSize { get; init; }
    public required int MaxHeaderListSize { get; init; }
    public required int MaxHeaderCount { get; init; }

    public required int BodyBufferThreshold { get; init; }
    public required int ResponseBodyChunkSize { get; init; }
    public required TimeSpan BodyConsumptionTimeout { get; init; }
}
