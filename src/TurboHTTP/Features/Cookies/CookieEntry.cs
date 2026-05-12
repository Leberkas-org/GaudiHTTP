namespace TurboHTTP.Features.Cookies;

internal sealed record CookieEntry(
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