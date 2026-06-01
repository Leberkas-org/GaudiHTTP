using TurboHTTP.Features.Caching;

namespace TurboHTTP.Client;

public sealed class CacheOptions
{
    /// <summary>Maximum number of entries held in the LRU store. Default 1 000.</summary>
    public int MaxEntries { get; set; } = 1000;

    /// <summary>
    /// Maximum body size (in bytes) for a single stored response. Default 50 MiB.
    /// Responses larger than this limit are not cached.
    /// </summary>
    public long MaxBodySize { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    /// When true the cache acts as a shared (proxy) cache: s-maxage is honoured,
    /// private responses are not stored.
    /// When false (default) the cache acts as a private (client-side) cache.
    /// RFC 9111 §3.1.
    /// </summary>
    public bool SharedCache { get; set; }

    internal CachePolicy To() => new()
    {
        MaxEntries = MaxEntries,
        MaxBodyBytes = MaxBodySize,
        SharedCache = SharedCache,
    };
}