namespace GaudiHTTP.Protocol.Syntax.Http3.Options;

internal sealed record Http3ClientEncoderOptions
{
    public required int QpackMaxTableCapacity { get; init; }
    public required int QpackBlockedStreams { get; init; }
}
