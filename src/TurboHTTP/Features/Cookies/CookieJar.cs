using System.Net;
using TurboHTTP.Protocol;

namespace TurboHTTP.Features.Cookies;

internal sealed class CookieJar(ICookieStore store)
{
    private readonly List<CookieStoreEntry> _applicable = [];

    public CookieJar()
        : this(new MemoryCookieStore())
    {
    }

    public void ProcessResponse(Uri requestUri, HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(response);

        var now = DateTimeOffset.UtcNow;

        if (!response.Headers.TryGetValues(WellKnownHeaders.SetCookie, out var setCookieValues))
        {
            return;
        }

        foreach (var header in setCookieValues)
        {
            var entry = CookieParser.Parse(header, requestUri, now);
            if (entry is null)
            {
                continue;
            }

            store.Remove(entry.Name, entry.Domain, entry.Path);

            if (!IsExpired(entry, now))
            {
                store.Add(ToStoreEntry(entry));
            }
        }
    }

    public void AddCookiesToRequest(Uri requestUri, ref HttpRequestMessage request)
        => AddCookiesToRequest(requestUri, ref request, firstPartyContext: null, isSafeMethod: true);

    /// <summary>
    /// Injects applicable cookies into <paramref name="request"/>, enforcing the <c>SameSite</c> attribute
    /// relative to the request's first-party context (RFC 6265bis §5.8.3).
    /// </summary>
    /// <param name="firstPartyContext">
    /// The site initiating the request. When <see langword="null"/> the request is treated as first-party
    /// (same-site), preserving the behavior of the simple two-argument overload.
    /// </param>
    /// <param name="isSafeMethod">
    /// Whether the request uses a safe, top-level-navigation method (GET/HEAD). <c>SameSite=Lax</c> cookies
    /// are sent cross-site only when this is <see langword="true"/>.
    /// </param>
    public void AddCookiesToRequest(Uri requestUri, ref HttpRequestMessage request, Uri? firstPartyContext, bool isSafeMethod)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        var requestHost = requestUri.Host.ToLowerInvariant();
        var requestPath = string.IsNullOrEmpty(requestUri.AbsolutePath) ? "/" : requestUri.AbsolutePath;
        var isHttps = requestUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        var isCrossSite = firstPartyContext is not null && !IsSameSite(requestUri, firstPartyContext);

        _applicable.Clear();

        foreach (var cookie in store.GetAll())
        {
            if (IsExpired(cookie, now))
            {
                continue;
            }

            if (cookie.Secure && !isHttps)
            {
                continue;
            }

            if (!DomainMatches(cookie.Domain, cookie.IsHostOnly, requestHost))
            {
                continue;
            }

            if (!PathMatches(cookie.Path, requestPath))
            {
                continue;
            }

            if (isCrossSite && !SameSiteAllowsCrossSite(cookie.SameSite, isSafeMethod))
            {
                continue;
            }

            _applicable.Add(cookie);
        }

        if (_applicable.Count == 0)
        {
            return;
        }

        _applicable.Sort((a, b) =>
        {
            var pathLenCmp = b.Path.Length.CompareTo(a.Path.Length);
            if (pathLenCmp != 0)
            {
                return pathLenCmp;
            }

            return a.CreatedAt.CompareTo(b.CreatedAt);
        });

        var parts = new string[_applicable.Count];
        for (var i = 0; i < _applicable.Count; i++)
        {
            parts[i] = $"{_applicable[i].Name}={_applicable[i].Value}";
        }

        request.Headers.TryAddWithoutValidation(WellKnownHeaders.Cookie,
            string.Join(WellKnownHeaders.SemiColonSpace, parts));
    }

    public int Count => store.Count;

    public void Clear() => store.Clear();

    /// <summary>
    /// Whether <c>SameSite</c> permits a cookie on a cross-site request.
    /// <c>Strict</c> never; <c>Lax</c> only on safe top-level navigations; <c>None</c>/<c>Unspecified</c> always.
    /// </summary>
    private static bool SameSiteAllowsCrossSite(SameSitePolicy policy, bool isSafeMethod) => policy switch
    {
        SameSitePolicy.Strict => false,
        SameSitePolicy.Lax => isSafeMethod,
        _ => true
    };

    /// <summary>
    /// Two URIs are same-site when they share the same registrable domain (RFC 6265bis §5.2).
    /// Uses a last-two-labels approximation; multi-level public suffixes (e.g. <c>co.uk</c>) are not
    /// resolved because TurboHTTP does not bundle a Public Suffix List.
    /// </summary>
    internal static bool IsSameSite(Uri request, Uri firstParty)
        => string.Equals(
            RegistrableDomain(request.Host),
            RegistrableDomain(firstParty.Host),
            StringComparison.OrdinalIgnoreCase);

    internal static string RegistrableDomain(string host)
    {
        if (IsIpAddress(host))
        {
            return host;
        }

        var labels = host.Split('.');
        return labels.Length <= 2
            ? host
            : string.Concat(labels[^2], ".", labels[^1]);
    }

    internal static bool DomainMatches(string cookieDomain, bool isHostOnly, string requestHost)
    {
        if (isHostOnly)
        {
            return string.Equals(cookieDomain, requestHost, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(cookieDomain, requestHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsIpAddress(requestHost))
        {
            return false;
        }

        return requestHost.EndsWith("." + cookieDomain, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool PathMatches(string cookiePath, string requestPath)
    {
        if (string.Equals(cookiePath, requestPath, StringComparison.Ordinal))
        {
            return true;
        }

        if (!requestPath.StartsWith(cookiePath, StringComparison.Ordinal))
        {
            return false;
        }

        if (cookiePath.EndsWith('/'))
        {
            return true;
        }

        if (requestPath.Length > cookiePath.Length && requestPath[cookiePath.Length] == '/')
        {
            return true;
        }

        return false;
    }

    private static bool IsExpired(CookieEntry cookie, DateTimeOffset now)
    {
        return cookie.ExpiresAt.HasValue && cookie.ExpiresAt.Value <= now;
    }

    private static bool IsExpired(CookieStoreEntry cookie, DateTimeOffset now)
    {
        return cookie.ExpiresAt.HasValue && cookie.ExpiresAt.Value <= now;
    }

    private static bool IsIpAddress(string host)
    {
        return IPAddress.TryParse(host, out _);
    }

    private static CookieStoreEntry ToStoreEntry(CookieEntry entry) => new(
        entry.Name,
        entry.Value,
        entry.Domain,
        entry.Path,
        entry.ExpiresAt,
        entry.Secure,
        entry.HttpOnly,
        entry.SameSite,
        entry.IsHostOnly,
        entry.CreatedAt);
}