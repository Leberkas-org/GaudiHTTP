namespace TurboHTTP.Protocol.Syntax.Http11.Options;

internal sealed record Http11ClientDecoderOptions
{
    public SharedHttpOptions Shared { get; init; } = SharedHttpOptions.Default;
    public int MaxPipelineDepth { get; init; } = 1;

    public static Http11ClientDecoderOptions Default { get; } = new();

    public void Validate()
    {
        if (MaxPipelineDepth <= 0)
        {
            throw new ArgumentException("MaxPipelineDepth must be greater than zero.", nameof(MaxPipelineDepth));
        }

        if (Shared is null)
        {
            throw new ArgumentException("Shared must not be null.", nameof(Shared));
        }

        Shared.Validate();
    }
}
