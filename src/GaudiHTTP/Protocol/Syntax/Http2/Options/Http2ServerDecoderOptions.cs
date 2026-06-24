namespace TurboHTTP.Protocol.Syntax.Http2.Options;

internal sealed record Http2ServerDecoderOptions
{
    public required int HeaderTableSize { get; init; }
    public required int MaxConcurrentStreams { get; init; }
    public required int MaxFieldSectionSize { get; init; }
    public required int MaxHeaderBytes { get; init; }
    public required int MaxHeaderCount { get; init; }
    public int InitialConnectionWindowSize { get; init; } = 1 * 1024 * 1024;
    public int InitialStreamWindowSize { get; init; } = 768 * 1024;
    public int MaxStreamWindowSize { get; init; } = 8 * 1024 * 1024;
    public double WindowScaleThresholdMultiplier { get; init; } = 1.0;
    public bool EnableAdaptiveWindowScaling { get; init; }
}
