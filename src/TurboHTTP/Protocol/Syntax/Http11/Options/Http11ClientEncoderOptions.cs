namespace TurboHTTP.Protocol.Syntax.Http11.Options;

internal sealed record Http11ClientEncoderOptions
{
    public SharedHttpOptions Shared { get; init; } = SharedHttpOptions.Default;
    public bool AutoHost { get; init; } = true;
    public bool AutoAcceptEncoding { get; init; } = true;

    public static Http11ClientEncoderOptions Default { get; } = new();

    public void Validate()
    {
        if (Shared is null)
        {
            throw new ArgumentException("Shared must not be null.", nameof(Shared));
        }

        Shared.Validate();
    }
}
