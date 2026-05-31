namespace TurboHTTP.Protocol.Syntax.Http2.Options;

internal sealed record Http2ClientDecoderOptions
{
    public int MaxConcurrentStreams { get; init; } = 100;
    public int InitialConnectionWindowSize { get; init; } = 64 * 1024 * 1024;
    public int InitialStreamWindowSize { get; init; } = 2 * 1024 * 1024;

    public static Http2ClientDecoderOptions Default { get; } = new();

    public void Validate()
    {
        if (MaxConcurrentStreams <= 0)
        {
            throw new ArgumentException("MaxConcurrentStreams must be > 0.", nameof(MaxConcurrentStreams));
        }

        if (InitialConnectionWindowSize <= 0)
        {
            throw new ArgumentException("InitialConnectionWindowSize must be > 0.", nameof(InitialConnectionWindowSize));
        }

        if (InitialStreamWindowSize <= 0)
        {
            throw new ArgumentException("InitialStreamWindowSize must be > 0.", nameof(InitialStreamWindowSize));
        }
    }
}
