namespace TurboHTTP.Features.Cookies;

public interface ICookieStore
{
    IReadOnlyList<CookieStoreEntry> GetAll();
    void Add(CookieStoreEntry entry);
    void Remove(string name, string domain, string path);
    void Clear();
    int Count { get; }
}
