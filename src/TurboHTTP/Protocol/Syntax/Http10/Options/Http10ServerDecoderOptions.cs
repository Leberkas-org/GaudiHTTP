namespace TurboHTTP.Protocol.Syntax.Http10.Options;

internal sealed record Http10ServerDecoderOptions
{
    public SharedHttpOptions Shared { get; init; } = SharedHttpOptions.Default;

    public static Http10ServerDecoderOptions Default { get; } = new();

    public void Validate()
    {
        if (Shared is null)
        {
            throw new ArgumentException("Http10ServerDecoderOptions.Shared must not be null.");
        }

        Shared.Validate();
    }
}
