namespace TurboHTTP.Protocol.Syntax.Http3.Options;

internal sealed record Http3ClientEncoderOptions
{
    public int QpackMaxTableCapacity { get; init; } = 16 * 1024;
    public int QpackBlockedStreams { get; init; } = 100;

    public static Http3ClientEncoderOptions Default { get; } = new();

    public void Validate()
    {
        if (QpackMaxTableCapacity < 0)
        {
            throw new ArgumentException("QpackMaxTableCapacity must be >= 0.", nameof(QpackMaxTableCapacity));
        }

        if (QpackBlockedStreams < 0)
        {
            throw new ArgumentException("QpackBlockedStreams must be >= 0.", nameof(QpackBlockedStreams));
        }
    }
}
