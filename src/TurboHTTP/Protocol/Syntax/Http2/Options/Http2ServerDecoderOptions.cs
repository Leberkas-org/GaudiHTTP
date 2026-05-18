namespace TurboHTTP.Protocol.Syntax.Http2.Options;

internal sealed record Http2ServerDecoderOptions
{
    public SharedHttpOptions Shared { get; init; } = SharedHttpOptions.Default;
    public int MaxConcurrentStreams { get; init; } = 100;
    public int MaxFieldSectionSize { get; init; } = 64 * 1024;

    public static Http2ServerDecoderOptions Default { get; } = new();

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

        if (Shared is null)
        {
            throw new ArgumentException("Shared must not be null.", nameof(Shared));
        }

        Shared.Validate();
    }
}
