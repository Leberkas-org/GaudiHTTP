namespace TurboHTTP.Protocol.Cookies;

internal sealed class MemoryCookieStore : ICookieStore
{
    private readonly List<CookieStoreEntry> _entries = [];

    public IReadOnlyList<CookieStoreEntry> GetAll() => _entries;

    public void Add(CookieStoreEntry entry) => _entries.Add(entry);

    public void Remove(string name, string domain, string path)
    {
        _entries.RemoveAll(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Domain, domain, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Path, path, StringComparison.Ordinal));
    }

    public void Clear() => _entries.Clear();

    public int Count => _entries.Count;
}
