namespace TurboHTTP.Protocol.Syntax.Http3.Options;

internal sealed record Http3ClientDecoderOptions
{
    public int MaxConcurrentStreams { get; init; } = 100;
    public int MaxFieldSectionSize { get; init; } = 64 * 1024;

    public static Http3ClientDecoderOptions Default { get; } = new();

    public void Validate()
    {
        if (MaxConcurrentStreams <= 0)
        {
            throw new ArgumentException("MaxConcurrentStreams must be > 0.", nameof(MaxConcurrentStreams));
        }

        if (MaxFieldSectionSize <= 0)
        {
            throw new ArgumentException("MaxFieldSectionSize must be > 0.", nameof(MaxFieldSectionSize));
        }
    }
}
