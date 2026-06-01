namespace TurboHTTP.Protocol.Syntax.Http2.Options;

internal sealed record Http2ClientDecoderOptions
{
    public int MaxConcurrentStreams { get; init; } = 100;
    public int InitialConnectionWindowSize { get; init; } = 64 * 1024 * 1024;
    public int InitialStreamWindowSize { get; init; } = 65535;
    public int MaxStreamWindowSize { get; init; } = 16 * 1024 * 1024;
    public double WindowScaleThresholdMultiplier { get; init; } = 1.0;
    public bool EnableAdaptiveWindowScaling { get; init; } = true;

    public static Http2ClientDecoderOptions Default { get; } = new();
}
