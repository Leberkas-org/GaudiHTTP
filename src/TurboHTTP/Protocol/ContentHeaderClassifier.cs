namespace TurboHTTP.Protocol;

internal static class ContentHeaderClassifier
{
    private static readonly HashSet<string> ContentHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnownHeaders.ContentType,
        WellKnownHeaders.ContentLength,
        WellKnownHeaders.ContentEncoding,
        WellKnownHeaders.ContentLanguage,
        WellKnownHeaders.ContentLocation,
        WellKnownHeaders.ContentMd5,
        WellKnownHeaders.ContentRange,
        WellKnownHeaders.ContentDisposition,
        WellKnownHeaders.Allow,
        WellKnownHeaders.Expires,
        WellKnownHeaders.LastModified
    };

    private static readonly HashSet<string> ForbiddenConnectionHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnownHeaders.Connection,
        WellKnownHeaders.TransferEncoding,
        WellKnownHeaders.Upgrade,
        WellKnownHeaders.ProxyConnection,
        WellKnownHeaders.KeepAliveHeader,
        WellKnownHeaders.Te
    };

    private static readonly Dictionary<string, string> ForbiddenConnectionHeadersExcludingTeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [WellKnownHeaders.Connection] = WellKnownHeaders.Connection,
            [WellKnownHeaders.TransferEncoding] = WellKnownHeaders.TransferEncoding,
            [WellKnownHeaders.Upgrade] = WellKnownHeaders.Upgrade,
            [WellKnownHeaders.ProxyConnection] = WellKnownHeaders.ProxyConnection,
            [WellKnownHeaders.KeepAliveHeader] = WellKnownHeaders.KeepAliveHeader
        };

    public static bool IsContentHeader(string name) => ContentHeaders.Contains(name);

    public static bool IsForbiddenConnectionHeader(string name) => ForbiddenConnectionHeaders.Contains(name);

    public static bool IsForbiddenConnectionHeaderExcludingTe(string name)
        => ForbiddenConnectionHeadersExcludingTeMap.ContainsKey(name);

    public static bool TryGetForbiddenCanonicalName(string name, out string canonicalName)
        => ForbiddenConnectionHeadersExcludingTeMap.TryGetValue(name, out canonicalName!);

    private static readonly Dictionary<string, string> LowerCaseCache = new(StringComparer.OrdinalIgnoreCase)
    {
        [WellKnownHeaders.ContentType] = "content-type",
        [WellKnownHeaders.ContentLength] = "content-length",
        [WellKnownHeaders.ContentEncoding] = "content-encoding",
        [WellKnownHeaders.ContentLanguage] = "content-language",
        [WellKnownHeaders.ContentLocation] = "content-location",
        [WellKnownHeaders.ContentRange] = "content-range",
        [WellKnownHeaders.ContentDisposition] = "content-disposition",
        [WellKnownHeaders.CacheControl] = "cache-control",
        [WellKnownHeaders.Date] = "date",
        [WellKnownHeaders.Server] = "server",
        [WellKnownHeaders.SetCookie] = "set-cookie",
        [WellKnownHeaders.TransferEncoding] = "transfer-encoding",
        [WellKnownHeaders.ETag] = "etag",
        [WellKnownHeaders.LastModified] = "last-modified",
        [WellKnownHeaders.Location] = "location",
        [WellKnownHeaders.Vary] = "vary",
        [WellKnownHeaders.AcceptRanges] = "accept-ranges",
        [WellKnownHeaders.AccessControlAllowOrigin] = "access-control-allow-origin",
        [WellKnownHeaders.AccessControlAllowMethods] = "access-control-allow-methods",
        [WellKnownHeaders.AccessControlAllowHeaders] = "access-control-allow-headers",
        [WellKnownHeaders.XContentTypeOptions] = "x-content-type-options",
        [WellKnownHeaders.StrictTransportSecurity] = "strict-transport-security",
        // Standard request headers (RFC 9110) — avoids re-lowercasing on every client request.
        [WellKnownHeaders.Host] = "host",
        [WellKnownHeaders.UserAgent] = "user-agent",
        [WellKnownHeaders.Accept] = "accept",
        [WellKnownHeaders.AcceptEncoding] = "accept-encoding",
        [WellKnownHeaders.AcceptLanguage] = "accept-language",
        [WellKnownHeaders.AcceptCharset] = "accept-charset",
        [WellKnownHeaders.Authorization] = "authorization",
        [WellKnownHeaders.Cookie] = "cookie",
        [WellKnownHeaders.Connection] = "connection",
        [WellKnownHeaders.Referer] = "referer",
        [WellKnownHeaders.Origin] = "origin",
        [WellKnownHeaders.Range] = "range",
        [WellKnownHeaders.Expect] = "expect",
        [WellKnownHeaders.IfMatch] = "if-match",
        [WellKnownHeaders.IfNoneMatch] = "if-none-match",
        [WellKnownHeaders.IfModifiedSince] = "if-modified-since",
        [WellKnownHeaders.IfUnmodifiedSince] = "if-unmodified-since",
        [WellKnownHeaders.IfRange] = "if-range",
        [WellKnownHeaders.Pragma] = "pragma",
        [WellKnownHeaders.Te] = "te",
        [WellKnownHeaders.UpgradeInsecureRequests] = "upgrade-insecure-requests",
        [WellKnownHeaders.XForwardedFor] = "x-forwarded-for",
        [WellKnownHeaders.XForwardedProto] = "x-forwarded-proto",
        ["X-Forwarded-Host"] = "x-forwarded-host",
        ["X-Requested-With"] = "x-requested-with",
        [WellKnownHeaders.Forwarded] = "forwarded",
        [WellKnownHeaders.From] = "from",
        [WellKnownHeaders.MaxForwards] = "max-forwards",
    };

    public static string ToLowerAscii(string name)
    {
        if (LowerCaseCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        if (!name.AsSpan().ContainsAnyInRange('A', 'Z'))
        {
            return name;
        }

        return string.Create(name.Length, name, static (span, src) => { System.Text.Ascii.ToLower(src, span, out _); });
    }

    public static string JoinHeaderValues(IEnumerable<string> values)
    {
        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return string.Empty;
        }

        var first = enumerator.Current;
        if (!enumerator.MoveNext())
        {
            return first;
        }

        var second = enumerator.Current;
        if (!enumerator.MoveNext())
        {
            return string.Concat(first, WellKnownHeaders.CommaSpace, second);
        }

        var parts = new List<string>(4) { first, second, enumerator.Current };
        var totalLength = first.Length + second.Length + enumerator.Current.Length + 4;

        while (enumerator.MoveNext())
        {
            totalLength += 2 + enumerator.Current.Length;
            parts.Add(enumerator.Current);
        }

        return string.Create(totalLength, parts, static (span, state) =>
        {
            var pos = 0;
            state[0].AsSpan().CopyTo(span);
            pos += state[0].Length;

            for (var i = 1; i < state.Count; i++)
            {
                span[pos] = ',';
                span[pos + 1] = ' ';
                pos += 2;
                state[i].AsSpan().CopyTo(span[pos..]);
                pos += state[i].Length;
            }
        });
    }
}