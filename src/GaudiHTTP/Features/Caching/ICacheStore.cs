using System.Diagnostics.CodeAnalysis;

namespace GaudiHTTP.Features.Caching;

/// <summary>
/// Pluggable storage back-end for cached HTTP response entries.
/// Implementations are responsible for memory management; <see cref="Set"/> takes ownership
/// of the provided <see cref="CacheStoreEntry"/> and <see cref="Remove"/>/<see cref="Clear"/>
/// must dispose evicted entries.
/// </summary>
public interface ICacheStore : IDisposable
{
    /// <summary>Attempts to retrieve the entry stored under <paramref name="key"/>. Returns <see langword="true"/> if found.</summary>
    bool TryGet(string key, [NotNullWhen(true)] out CacheStoreEntry? entry);

    /// <summary>Stores <paramref name="entry"/> under <paramref name="key"/>, replacing any existing entry.</summary>
    void Set(string key, CacheStoreEntry entry);

    /// <summary>Removes and disposes the entry stored under <paramref name="key"/>. Returns <see langword="true"/> if an entry was present.</summary>
    bool Remove(string key);

    /// <summary>Removes and disposes all stored entries.</summary>
    void Clear();
}