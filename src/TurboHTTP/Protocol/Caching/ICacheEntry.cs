namespace TurboHTTP.Protocol.Caching;

public interface ICacheEntry : IDisposable
{
    HttpResponseMessage Response { get; }
    ReadOnlyMemory<byte> Body { get; }
    DateTimeOffset RequestTime { get; }
    DateTimeOffset ResponseTime { get; }
    string? ETag { get; }
    DateTimeOffset? LastModified { get; }
    DateTimeOffset? Expires { get; }
    DateTimeOffset? Date { get; }
    int? AgeSeconds { get; }
    CacheControl? CacheControl { get; }
    IReadOnlyList<string> VaryHeaderNames { get; }
    IReadOnlyDictionary<string, string?> VaryRequestValues { get; }
}
