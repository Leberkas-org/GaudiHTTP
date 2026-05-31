namespace TurboHTTP.Protocol.Syntax.Http3.Options;

internal sealed record Http3ClientEncoderOptions
{
    public int QpackMaxTableCapacity { get; init; } = 16 * 1024;
    public int QpackBlockedStreams { get; init; } = 100;

    public static Http3ClientEncoderOptions Default { get; } = new();
}
