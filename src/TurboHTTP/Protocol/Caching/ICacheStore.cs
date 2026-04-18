using System.Buffers;

namespace TurboHTTP.Protocol.Caching;

public interface ICacheStore
{
    /// <summary>
    /// RFC 9111 §4 — Looks up a matching entry for the request.
    /// Returns null on a cache miss. Respects the Vary header for variant selection.
    /// </summary>
    public ICacheEntry? Get(HttpRequestMessage request);

    /// <summary>
    /// RFC 9111 §3 — Stores a cacheable response. Respects MaxEntries (LRU eviction)
    /// and MaxBodyBytes. Does nothing if the response should not be stored.
    /// </summary>
    public void Put(HttpRequestMessage request, HttpResponseMessage response, IMemoryOwner<byte> bodyOwner,
        int bodyLength,
        DateTimeOffset requestTime,
        DateTimeOffset responseTime);

    /// <summary>
    /// RFC 9111 §4.4 — Invalidates all stored entries whose URI matches the given URI.
    /// Called after unsafe methods (POST, PUT, DELETE, PATCH) that may have modified the resource.
    /// </summary>
    public void Invalidate(Uri uri);
}