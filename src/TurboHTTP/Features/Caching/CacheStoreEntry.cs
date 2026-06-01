namespace TurboHTTP.Features.Caching;

/// <summary>
/// Mutable store record that owns a cached HTTP response and its body buffer.
/// Passed into and out of <see cref="ICacheStore"/>. Dispose to release the body buffer.
/// </summary>
public sealed class CacheStoreEntry : IDisposable
{
    /// <summary>Gets the cached HTTP response message.</summary>
    public required HttpResponseMessage Response { get; init; }

    /// <summary>Gets the pooled buffer containing the cached response body.</summary>
    public required CacheBody Body { get; init; }

    /// <summary>Gets the time at which the originating request was sent (RFC 9111 §4.2.3).</summary>
    public required DateTimeOffset RequestTime { get; init; }

    /// <summary>Gets the time at which the response was received (RFC 9111 §4.2.3).</summary>
    public required DateTimeOffset ResponseTime { get; init; }

    /// <summary>Gets the ETag validator from the cached response, or <see langword="null"/> if absent.</summary>
    public string? ETag { get; init; }

    /// <summary>Gets the Last-Modified date from the cached response, or <see langword="null"/> if absent.</summary>
    public DateTimeOffset? LastModified { get; init; }

    /// <summary>Gets the Expires date from the cached response, or <see langword="null"/> if absent.</summary>
    public DateTimeOffset? Expires { get; init; }

    /// <summary>Gets the Date header value from the cached response, or <see langword="null"/> if absent.</summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>Gets the Age header value in seconds from the cached response, or <see langword="null"/> if absent.</summary>
    public int? AgeSeconds { get; init; }

    /// <summary>Gets the parsed Cache-Control directives from the cached response, or <see langword="null"/> if absent.</summary>
    public CacheControlStoreEntry? CacheControl { get; init; }

    /// <summary>Gets the header names from the Vary field of the cached response. Defaults to an empty array.</summary>
    public string[] VaryHeaderNames { get; init; } = [];

    /// <summary>Gets the request header values captured at store time for each Vary header name. Defaults to an empty dictionary.</summary>
    public Dictionary<string, string?> VaryRequestValues { get; init; } = new();

    /// <summary>Disposes the underlying body buffer.</summary>
    public void Dispose() => Body.Dispose();
}

/// <summary>
/// Serializable snapshot of Cache-Control directives stored alongside a cached response.
/// Mirrors <see cref="CacheControl"/> but uses plain arrays instead of <see cref="IReadOnlyList{T}"/>
/// to simplify persistence and equality semantics.
/// </summary>
public sealed record CacheControlStoreEntry
{
    /// <summary>RFC 9111 §5.2.1.4 / §5.2.2.3 — no-cache directive.</summary>
    public bool NoCache { get; init; }

    /// <summary>RFC 9111 §5.2.1.5 / §5.2.2.4 — no-store directive.</summary>
    public bool NoStore { get; init; }

    /// <summary>RFC 9111 §5.2.1.6 / §5.2.2.5 — no-transform directive.</summary>
    public bool NoTransform { get; init; }

    /// <summary>RFC 9111 §5.2.1.1 / §5.2.2.1 — max-age value.</summary>
    public TimeSpan? MaxAge { get; init; }

    /// <summary>RFC 9111 §5.2.1.2 — max-stale value (request only).</summary>
    public TimeSpan? MaxStale { get; init; }

    /// <summary>RFC 9111 §5.2.1.3 — min-fresh value (request only).</summary>
    public TimeSpan? MinFresh { get; init; }

    /// <summary>RFC 9111 §5.2.1.7 — only-if-cached directive (request only).</summary>
    public bool OnlyIfCached { get; init; }

    /// <summary>RFC 9111 §5.2.2.9 — s-maxage value (response, shared cache only).</summary>
    public TimeSpan? SMaxAge { get; init; }

    /// <summary>RFC 9111 §5.2.2.2 — must-revalidate directive (response only).</summary>
    public bool MustRevalidate { get; init; }

    /// <summary>RFC 9111 §5.2.2.7 — proxy-revalidate directive (response only).</summary>
    public bool ProxyRevalidate { get; init; }

    /// <summary>RFC 9111 §5.2.2.8 — public directive (response only).</summary>
    public bool Public { get; init; }

    /// <summary>RFC 9111 §5.2.2.6 — private directive (response only).</summary>
    public bool Private { get; init; }

    /// <summary>RFC 8246 — immutable directive (response only).</summary>
    public bool Immutable { get; init; }

    /// <summary>RFC 9111 §5.2.2.3 — must-understand directive (response only).</summary>
    public bool MustUnderstand { get; init; }

    /// <summary>RFC 9111 §5.2.2.3 — field names from no-cache="…". Defaults to an empty array.</summary>
    public string[] NoCacheFields { get; init; } = [];

    /// <summary>RFC 9111 §5.2.2.6 — field names from private="…". Defaults to an empty array.</summary>
    public string[] PrivateFields { get; init; } = [];

    internal CacheControl ToCacheControl() => new()
    {
        NoCache = NoCache,
        NoStore = NoStore,
        NoTransform = NoTransform,
        MaxAge = MaxAge,
        MaxStale = MaxStale,
        MinFresh = MinFresh,
        OnlyIfCached = OnlyIfCached,
        SMaxAge = SMaxAge,
        MustRevalidate = MustRevalidate,
        ProxyRevalidate = ProxyRevalidate,
        Public = Public,
        Private = Private,
        Immutable = Immutable,
        MustUnderstand = MustUnderstand,
        NoCacheFields = NoCacheFields,
        PrivateFields = PrivateFields,
    };

    internal static CacheControlStoreEntry FromCacheControl(CacheControl cc) => new()
    {
        NoCache = cc.NoCache,
        NoStore = cc.NoStore,
        NoTransform = cc.NoTransform,
        MaxAge = cc.MaxAge,
        MaxStale = cc.MaxStale,
        MinFresh = cc.MinFresh,
        OnlyIfCached = cc.OnlyIfCached,
        SMaxAge = cc.SMaxAge,
        MustRevalidate = cc.MustRevalidate,
        ProxyRevalidate = cc.ProxyRevalidate,
        Public = cc.Public,
        Private = cc.Private,
        Immutable = cc.Immutable,
        MustUnderstand = cc.MustUnderstand,
        NoCacheFields = cc.NoCacheFields?.ToArray() ?? [],
        PrivateFields = cc.PrivateFields?.ToArray() ?? [],
    };
}