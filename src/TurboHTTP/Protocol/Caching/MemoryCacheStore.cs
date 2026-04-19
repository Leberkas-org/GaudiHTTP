using System.Diagnostics.CodeAnalysis;

namespace TurboHTTP.Protocol.Caching;

internal sealed class MemoryCacheStore : ICacheStore
{
    private readonly Dictionary<string, CacheStoreEntry> _entries = new();

    public bool TryGet(string key, [NotNullWhen(true)] out CacheStoreEntry? entry)
    {
        return _entries.TryGetValue(key, out entry);
    }

    public void Set(string key, CacheStoreEntry entry)
    {
        _entries[key] = entry;
    }

    public bool Remove(string key)
    {
        var result = _entries.Remove(key, out var item);
        item?.Dispose();
        return result;
    }

    public void Clear()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Dispose();
        }

        _entries.Clear();
    }

    public void Dispose() => Clear();
}