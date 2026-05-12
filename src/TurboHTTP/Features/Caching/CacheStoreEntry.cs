namespace TurboHTTP.Features.Caching;

public sealed class CacheStoreEntry : IDisposable
{
    public required HttpResponseMessage Response { get; init; }
    public required CacheBody Body { get; init; }
    public required DateTimeOffset RequestTime { get; init; }
    public required DateTimeOffset ResponseTime { get; init; }
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public DateTimeOffset? Expires { get; init; }
    public DateTimeOffset? Date { get; init; }
    public int? AgeSeconds { get; init; }
    public CacheControlStoreEntry? CacheControl { get; init; }
    public string[] VaryHeaderNames { get; init; } = [];
    public Dictionary<string, string?> VaryRequestValues { get; init; } = new();

    public void Dispose() => Body.Dispose();
}

public sealed record CacheControlStoreEntry
{
    public bool NoCache { get; init; }
    public bool NoStore { get; init; }
    public bool NoTransform { get; init; }
    public TimeSpan? MaxAge { get; init; }
    public TimeSpan? MaxStale { get; init; }
    public TimeSpan? MinFresh { get; init; }
    public bool OnlyIfCached { get; init; }
    public TimeSpan? SMaxAge { get; init; }
    public bool MustRevalidate { get; init; }
    public bool ProxyRevalidate { get; init; }
    public bool Public { get; init; }
    public bool Private { get; init; }
    public bool Immutable { get; init; }
    public bool MustUnderstand { get; init; }
    public string[] NoCacheFields { get; init; } = [];
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