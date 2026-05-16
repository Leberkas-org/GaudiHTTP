namespace TurboHTTP.Protocol.Syntax.Http10.Options;

internal sealed record Http10ClientEncoderOptions
{
    public SharedHttpOptions Shared { get; init; } = SharedHttpOptions.Default;

    public static Http10ClientEncoderOptions Default { get; } = new();

    public void Validate()
    {
        if (Shared is null)
        {
            throw new ArgumentException("Http10ClientEncoderOptions.Shared must not be null.");
        }

        Shared.Validate();
    }
}
