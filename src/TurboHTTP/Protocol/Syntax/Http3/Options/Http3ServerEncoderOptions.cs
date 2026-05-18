namespace TurboHTTP.Protocol.Syntax.Http3.Options;

internal sealed record Http3ServerEncoderOptions
{
    public SharedHttpOptions Shared { get; init; } = SharedHttpOptions.Default;
    public bool WriteDateHeader { get; init; } = true;
    public int QpackMaxTableCapacity { get; init; } = 16 * 1024;

    public static Http3ServerEncoderOptions Default { get; } = new();

    public void Validate()
    {
        if (Shared is null)
        {
            throw new ArgumentException("Shared must not be null.", nameof(Shared));
        }

        Shared.Validate();

        if (QpackMaxTableCapacity < 0)
        {
            throw new ArgumentException("QpackMaxTableCapacity must be >= 0.", nameof(QpackMaxTableCapacity));
        }
    }
}
