using System.Diagnostics.CodeAnalysis;

namespace TurboHTTP.Features.Caching;

public interface ICacheStore : IDisposable
{
    bool TryGet(string key, [NotNullWhen(true)] out CacheStoreEntry? entry);
    void Set(string key, CacheStoreEntry entry);
    bool Remove(string key);
    void Clear();
}