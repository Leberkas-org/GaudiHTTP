using System.Buffers;

namespace TurboHTTP.Protocol.Syntax;

internal sealed record SharedHttpOptions
{
    public long StreamingThreshold { get; init; } = 64 * 1024L;
    public long MaxBufferedBodySize { get; init; } = 4 * 1024 * 1024L;
    public long? MaxStreamedBodySize { get; init; }
    public int MaxHeaderBytes { get; init; } = 32 * 1024;
    public int MaxHeaderCount { get; init; } = 100;
    public int HeaderLineMaxLength { get; init; } = 8 * 1024;
    public int RequestLineMaxLength { get; init; } = 8 * 1024;
    public bool AllowObsFold { get; init; }
    public MemoryPool<byte> BufferPool { get; init; } = MemoryPool<byte>.Shared;

    public static SharedHttpOptions Default { get; } = new();

    public void Validate()
    {
        if (StreamingThreshold < 0)
        {
            throw new ArgumentException(
                $"SharedHttpOptions.StreamingThreshold must be >= 0 (got {StreamingThreshold}).");
        }

        if (MaxBufferedBodySize < 0)
        {
            throw new ArgumentException(
                $"SharedHttpOptions.MaxBufferedBodySize must be >= 0 (got {MaxBufferedBodySize}).");
        }

        if (MaxBufferedBodySize < StreamingThreshold)
        {
            throw new ArgumentException(
                $"SharedHttpOptions.MaxBufferedBodySize ({MaxBufferedBodySize}) must be >= StreamingThreshold ({StreamingThreshold}).");
        }

        if (MaxStreamedBodySize is < 0)
        {
            throw new ArgumentException(
                $"SharedHttpOptions.MaxStreamedBodySize must be null or >= 0 (got {MaxStreamedBodySize}).");
        }

        if (MaxHeaderBytes <= 0)
        {
            throw new ArgumentException(
                $"SharedHttpOptions.MaxHeaderBytes must be > 0 (got {MaxHeaderBytes}).");
        }

        if (MaxHeaderCount <= 0)
        {
            throw new ArgumentException(
                $"SharedHttpOptions.MaxHeaderCount must be > 0 (got {MaxHeaderCount}).");
        }

        if (HeaderLineMaxLength <= 0)
        {
            throw new ArgumentException(
                $"SharedHttpOptions.HeaderLineMaxLength must be > 0 (got {HeaderLineMaxLength}).");
        }

        if (RequestLineMaxLength <= 0)
        {
            throw new ArgumentException(
                $"SharedHttpOptions.RequestLineMaxLength must be > 0 (got {RequestLineMaxLength}).");
        }

        if (BufferPool is null)
        {
            throw new ArgumentException("SharedHttpOptions.BufferPool must not be null.");
        }
    }
}