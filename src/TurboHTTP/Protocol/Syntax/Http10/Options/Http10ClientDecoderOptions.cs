namespace TurboHTTP.Protocol.Syntax.Http10.Options;

internal sealed record Http10ClientDecoderOptions
{
    public SharedHttpOptions Shared { get; init; } = SharedHttpOptions.Default;

    public static Http10ClientDecoderOptions Default { get; } = new();

    public void Validate()
    {
        if (Shared is null)
        {
            throw new ArgumentException("Http10ClientDecoderOptions.Shared must not be null.");
        }

        Shared.Validate();
    }
}
