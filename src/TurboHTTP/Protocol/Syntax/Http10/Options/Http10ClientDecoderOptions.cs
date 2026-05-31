namespace TurboHTTP.Protocol.Syntax.Http10.Options;

internal sealed record Http10ClientDecoderOptions
{
    public long StreamingThreshold { get; init; } = 64 * 1024;
    public long MaxBufferedBodySize { get; init; } = 4 * 1024 * 1024;
    public long? MaxStreamedBodySize { get; init; }
    public int MaxHeaderBytes { get; init; } = 32 * 1024;
    public int MaxHeaderCount { get; init; } = 100;
    public int HeaderLineMaxLength { get; init; } = 8 * 1024;
    public bool AllowObsFold { get; init; }

    public static Http10ClientDecoderOptions Default { get; } = new();

    public void Validate()
    {
        if (StreamingThreshold < 0)
        {
            throw new ArgumentException("StreamingThreshold must be >= 0.", nameof(StreamingThreshold));
        }

        if (MaxBufferedBodySize < StreamingThreshold)
        {
            throw new ArgumentException("MaxBufferedBodySize must be >= StreamingThreshold.", nameof(MaxBufferedBodySize));
        }

        if (MaxStreamedBodySize is < 0)
        {
            throw new ArgumentException("MaxStreamedBodySize must be null or >= 0.", nameof(MaxStreamedBodySize));
        }

        if (MaxHeaderBytes <= 0)
        {
            throw new ArgumentException("MaxHeaderBytes must be > 0.", nameof(MaxHeaderBytes));
        }

        if (MaxHeaderCount <= 0)
        {
            throw new ArgumentException("MaxHeaderCount must be > 0.", nameof(MaxHeaderCount));
        }

        if (HeaderLineMaxLength <= 0)
        {
            throw new ArgumentException("HeaderLineMaxLength must be > 0.", nameof(HeaderLineMaxLength));
        }
    }
}
