namespace GaudiHTTP.Protocol.Syntax.Http2.Options;

internal sealed record Http2ClientDecoderOptions
{
    public required int MaxConcurrentStreams { get; init; }
    public required int InitialConnectionWindowSize { get; init; }
    public required int InitialStreamWindowSize { get; init; }
    public required int MaxStreamWindowSize { get; init; }
    public required double WindowScaleThresholdMultiplier { get; init; }
    public required bool EnableAdaptiveWindowScaling { get; init; }
    public required int MaxHeaderSize { get; init; }
    public required int MaxHeaderListSize { get; init; }
}
