namespace TurboHTTP.Protocol.Syntax.Http11.Options;

internal sealed record Http11ServerDecoderOptions
{
    public SharedHttpOptions Shared { get; init; } = SharedHttpOptions.Default;
    public int MaxPipelinedRequests { get; init; } = 10;

    public static Http11ServerDecoderOptions Default { get; } = new();

    public void Validate()
    {
        if (MaxPipelinedRequests <= 0)
        {
            throw new ArgumentException("MaxPipelinedRequests must be greater than zero.", nameof(MaxPipelinedRequests));
        }

        if (Shared is null)
        {
            throw new ArgumentException("Shared must not be null.", nameof(Shared));
        }

        Shared.Validate();
    }
}
