namespace GaudiHTTP.Features.Caching;

/// <summary>
/// Read-only view of a cached HTTP response entry, including the response metadata,
/// body bytes, and freshness validators. Dispose to release the underlying body buffer.
/// </summary>
public interface ICacheEntry : IDisposable
{
    /// <summary>Gets the cached HTTP response message.</summary>
    HttpResponseMessage Response { get; }

    /// <summary>Gets the cached response body as a read-only memory region.</summary>
    ReadOnlyMemory<byte> Body { get; }

    /// <summary>Gets the time at which the originating request was sent (RFC 9111 §4.2.3).</summary>
    DateTimeOffset RequestTime { get; }

    /// <summary>Gets the time at which the response was received (RFC 9111 §4.2.3).</summary>
    DateTimeOffset ResponseTime { get; }

    /// <summary>Gets the ETag validator from the cached response, or <see langword="null"/> if absent.</summary>
    string? ETag { get; }

    /// <summary>Gets the Last-Modified date from the cached response, or <see langword="null"/> if absent.</summary>
    DateTimeOffset? LastModified { get; }

    /// <summary>Gets the Expires date from the cached response, or <see langword="null"/> if absent.</summary>
    DateTimeOffset? Expires { get; }

    /// <summary>Gets the Date header value from the cached response, or <see langword="null"/> if absent.</summary>
    DateTimeOffset? Date { get; }

    /// <summary>Gets the Age header value in seconds from the cached response, or <see langword="null"/> if absent.</summary>
    int? AgeSeconds { get; }

    /// <summary>Gets the parsed Cache-Control directives from the cached response, or <see langword="null"/> if absent.</summary>
    CacheControl? CacheControl { get; }

    /// <summary>Gets the list of header names from the Vary field of the cached response.</summary>
    IReadOnlyList<string> VaryHeaderNames { get; }

    /// <summary>Gets the request header values captured at store time for each Vary header name.</summary>
    IReadOnlyDictionary<string, string?> VaryRequestValues { get; }
}
