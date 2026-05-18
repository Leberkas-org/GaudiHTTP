namespace TurboHTTP.Protocol.Syntax.Http2.Options;

internal sealed record Http2ClientEncoderOptions
{
    public SharedHttpOptions Shared { get; init; } = SharedHttpOptions.Default;
    public int HeaderTableSize { get; init; } = 64 * 1024;
    public int MaxFrameSize { get; init; } = 16 * 1024;

    public static Http2ClientEncoderOptions Default { get; } = new();

    public void Validate()
    {
        if (HeaderTableSize < 0)
        {
            throw new ArgumentException("HeaderTableSize must be >= 0.", nameof(HeaderTableSize));
        }

        if (MaxFrameSize is < 16 * 1024 or > (16 * 1024 * 1024) - 1)
        {
            throw new ArgumentException("MaxFrameSize must be between 16384 and 16777215.", nameof(MaxFrameSize));
        }

        if (Shared is null)
        {
            throw new ArgumentException("Shared must not be null.", nameof(Shared));
        }

        Shared.Validate();
    }
}
