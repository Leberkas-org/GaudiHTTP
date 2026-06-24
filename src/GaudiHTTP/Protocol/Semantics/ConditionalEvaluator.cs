namespace TurboHTTP.Protocol.Semantics;

/// <summary>
/// Result of a conditional request evaluation per RFC 9110 §13.2.
/// </summary>
internal enum PreconditionResult
{
    /// <summary>
    /// Preconditions are satisfied; request should proceed normally.
    /// </summary>
    Continue,

    /// <summary>
    /// Representation has not changed; return 304 Not Modified.
    /// </summary>
    NotModified,

    /// <summary>
    /// Preconditions failed; return 412 Precondition Failed.
    /// </summary>
    PreconditionFailed
}

/// <summary>
/// RFC 9110 §13.2 — Evaluates conditional request headers to determine whether
/// the server should proceed with the request, return 304 Not Modified, or return 412 Precondition Failed.
/// Evaluation order is critical: If-Match → If-None-Match → If-Unmodified-Since → If-Modified-Since.
/// </summary>
internal static class ConditionalEvaluator
{
    /// <summary>
    /// Evaluates conditional request headers in the order specified by RFC 9110 §13.2.
    /// Returns the result of the evaluation and the action the server should take.
    /// </summary>
    /// <param name="ifMatch">The If-Match header value (comma-separated ETags or "*"), or null if absent.</param>
    /// <param name="ifNoneMatch">The If-None-Match header value (comma-separated ETags or "*"), or null if absent.</param>
    /// <param name="ifModifiedSince">The If-Modified-Since header value as a DateTimeOffset, or null if absent.</param>
    /// <param name="ifUnmodifiedSince">The If-Unmodified-Since header value as a DateTimeOffset, or null if absent.</param>
    /// <param name="currentETag">The current ETag of the representation (e.g., "abc123"), or null if not available.</param>
    /// <param name="lastModified">The current Last-Modified date of the representation, or null if not available.</param>
    /// <param name="methodIsGetOrHead">True if the request method is GET or HEAD; used to determine 304 vs 412 responses.</param>
    /// <returns>A PreconditionResult indicating whether the request should continue, return 304, or return 412.</returns>
    public static PreconditionResult Evaluate(
        string? ifMatch = null,
        string? ifNoneMatch = null,
        DateTimeOffset? ifModifiedSince = null,
        DateTimeOffset? ifUnmodifiedSince = null,
        string? currentETag = null,
        DateTimeOffset? lastModified = null,
        bool methodIsGetOrHead = false)
    {
        // RFC 9110 §13.2: Evaluation order is critical.
        // 1. If-Match
        if (ifMatch is not null)
        {
            if (!MatchesETag(ifMatch, currentETag))
            {
                return PreconditionResult.PreconditionFailed;
            }
        }

        // 2. If-None-Match
        if (ifNoneMatch is not null)
        {
            if (MatchesETag(ifNoneMatch, currentETag))
            {
                // RFC 9110 §13.1.2: If If-None-Match is satisfied (matches), return 304 for GET/HEAD, else 412.
                return methodIsGetOrHead ? PreconditionResult.NotModified : PreconditionResult.PreconditionFailed;
            }
        }

        // 3. If-Unmodified-Since
        if (ifUnmodifiedSince is not null && lastModified is not null)
        {
            // Representation is deemed unmodified if last-modified <= if-unmodified-since.
            if (lastModified > ifUnmodifiedSince)
            {
                return PreconditionResult.PreconditionFailed;
            }
        }

        // 4. If-Modified-Since
        if (ifModifiedSince is not null && lastModified is not null && methodIsGetOrHead)
        {
            // For GET/HEAD: if representation not modified since if-modified-since, return 304.
            if (lastModified <= ifModifiedSince)
            {
                return PreconditionResult.NotModified;
            }
        }

        return PreconditionResult.Continue;
    }

    /// <summary>
    /// Checks if an ETag matches the list of ETags in a conditional header.
    /// Supports "*" (matches any) and comma-separated ETag lists.
    /// Uses strong matching for If-Match, weak matching for If-None-Match.
    /// This is a helper; the caller must determine which to use based on context.
    /// </summary>
    private static bool MatchesETag(string etagHeaderValue, string? currentETag)
    {
        if (currentETag is null)
        {
            return false;
        }

        if (etagHeaderValue == "*")
        {
            return true;
        }

        // Split by comma and check if any ETag matches.
        var etags = etagHeaderValue.Split(',');
        foreach (var etag in etags)
        {
            var trimmedETag = etag.Trim();
            // Use weak matching by default; callers should enforce strong matching if needed.
            if (ETagComparer.WeakMatch(trimmedETag, currentETag))
            {
                return true;
            }
        }

        return false;
    }
}
