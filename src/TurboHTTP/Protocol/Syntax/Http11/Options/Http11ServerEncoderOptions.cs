namespace TurboHTTP.Protocol.Syntax.Http11.Options;

internal sealed record Http11ServerEncoderOptions
{
    public SharedHttpOptions Shared { get; init; } = SharedHttpOptions.Default;
    public TimeSpan KeepAliveTimeout { get; init; } = TimeSpan.FromSeconds(120);
    public TimeSpan RequestHeadersTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool WriteDateHeader { get; init; } = true;

    public static Http11ServerEncoderOptions Default { get; } = new();

    public void Validate()
    {
        if (KeepAliveTimeout < TimeSpan.Zero)
        {
            throw new ArgumentException("KeepAliveTimeout must not be negative.", nameof(KeepAliveTimeout));
        }

        if (RequestHeadersTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("RequestHeadersTimeout must be greater than zero.", nameof(RequestHeadersTimeout));
        }

        if (Shared is null)
        {
            throw new ArgumentException("Shared must not be null.", nameof(Shared));
        }

        Shared.Validate();
    }
}
