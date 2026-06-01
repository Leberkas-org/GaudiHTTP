namespace TurboHTTP.Features.Cookies;

/// <summary>
/// Specifies the SameSite attribute of a cookie, controlling cross-site send behavior (RFC 6265bis §5.4).
/// </summary>
public enum SameSitePolicy
{
    /// <summary>No SameSite attribute was present; the cookie is sent in all contexts.</summary>
    Unspecified,

    /// <summary>The cookie is sent only for same-site requests.</summary>
    Strict,

    /// <summary>The cookie is sent for same-site requests and safe (GET/HEAD) top-level cross-site navigations.</summary>
    Lax,

    /// <summary>The cookie is sent in all contexts, including cross-site. Requires the Secure attribute.</summary>
    None,
}

/// <summary>
/// Immutable snapshot of a cookie as persisted in an <see cref="ICookieStore"/>.
/// </summary>
/// <param name="Name">The cookie name.</param>
/// <param name="Value">The cookie value.</param>
/// <param name="Domain">The domain the cookie applies to, lowercased and without a leading dot.</param>
/// <param name="Path">The path scope of the cookie (always starts with '/').</param>
/// <param name="ExpiresAt">Absolute UTC expiry time, or <see langword="null"/> for a session cookie.</param>
/// <param name="Secure">When <see langword="true"/>, the cookie is sent only over HTTPS.</param>
/// <param name="HttpOnly">When <see langword="true"/>, the cookie is inaccessible to client-side scripts.</param>
/// <param name="SameSite">The SameSite policy controlling cross-site delivery.</param>
/// <param name="IsHostOnly">When <see langword="true"/>, the cookie was set without a Domain attribute and applies to the exact request host only.</param>
/// <param name="CreatedAt">UTC time at which the cookie was first stored.</param>
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
