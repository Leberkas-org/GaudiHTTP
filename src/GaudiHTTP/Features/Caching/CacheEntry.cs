using System.Buffers;

namespace TurboHTTP.Features.Caching;

internal sealed class CacheEntry : ICacheEntry
{
    private IMemoryOwner<byte>? _bodyOwner;

    public required HttpResponseMessage Response { get; init; }

    public required IMemoryOwner<byte> BodyOwner
    {
        get => _bodyOwner!;
        init => _bodyOwner = value;
    }

    public required int BodyLength { get; init; }

    public ReadOnlyMemory<byte> Body => _bodyOwner!.Memory[..BodyLength];

    public required DateTimeOffset RequestTime { get; init; }
    public required DateTimeOffset ResponseTime { get; init; }
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public DateTimeOffset? Expires { get; init; }
    public DateTimeOffset? Date { get; init; }
    public int? AgeSeconds { get; init; }
    public CacheControl? CacheControl { get; init; }
    public IReadOnlyList<string> VaryHeaderNames { get; init; } = [];
    public IReadOnlyDictionary<string, string?> VaryRequestValues { get; init; } = new Dictionary<string, string?>();

    public void Dispose()
    {
        Interlocked.Exchange(ref _bodyOwner, null)?.Dispose();
    }
}
