namespace TurboHTTP.Features.Cookies;

/// <summary>
/// Pluggable storage back-end for cookie entries used by the cookie jar.
/// Implementations are not required to be thread-safe; the cookie jar accesses the
/// store on a single logical thread per request pipeline.
/// </summary>
public interface ICookieStore
{
    /// <summary>Returns all stored cookie entries.</summary>
    IReadOnlyList<CookieStoreEntry> GetAll();

    /// <summary>Adds <paramref name="entry"/> to the store.</summary>
    void Add(CookieStoreEntry entry);

    /// <summary>Removes the entry matching the given <paramref name="name"/>, <paramref name="domain"/>, and <paramref name="path"/> triple.</summary>
    void Remove(string name, string domain, string path);

    /// <summary>Removes all stored cookie entries.</summary>
    void Clear();

    /// <summary>Gets the number of cookie entries currently in the store.</summary>
    int Count { get; }
}
