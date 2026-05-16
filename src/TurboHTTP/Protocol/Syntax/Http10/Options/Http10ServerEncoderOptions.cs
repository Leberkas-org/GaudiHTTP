namespace TurboHTTP.Protocol.Syntax.Http10.Options;

internal sealed record Http10ServerEncoderOptions
{
    public SharedHttpOptions Shared { get; init; } = SharedHttpOptions.Default;
    public bool WriteDateHeader { get; init; } = true;

    public static Http10ServerEncoderOptions Default { get; } = new();

    public void Validate()
    {
        if (Shared is null)
        {
            throw new ArgumentException("Http10ServerEncoderOptions.Shared must not be null.");
        }

        Shared.Validate();
    }
}
