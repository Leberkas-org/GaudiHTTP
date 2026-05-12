namespace TurboHTTP.Features.Cookies;

public enum SameSitePolicy
{
    Unspecified,
    Strict,
    Lax,
    None,
}

public sealed record CookieStoreEntry(
    string Name,
    string Value,
    string Domain,
    string Path,
    DateTimeOffset? ExpiresAt,
    bool Secure,
    bool HttpOnly,
    SameSitePolicy SameSite,
    bool IsHostOnly,
    DateTimeOffset CreatedAt);
