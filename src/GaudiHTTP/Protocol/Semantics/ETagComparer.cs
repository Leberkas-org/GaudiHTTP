namespace TurboHTTP.Protocol.Semantics;

/// <summary>
/// RFC 9110 §8.8.3.2 — Compares entity-tags for HTTP conditional requests.
/// Supports both strong matching (exact opaque-tag match, rejects weak ETags)
/// and weak matching (ignores W/ prefix, treats "*" as universal match).
/// </summary>
internal static class ETagComparer
{
    /// <summary>
    /// Performs strong entity-tag comparison per RFC 9110 §8.8.3.2.
    /// Strong match requires exact opaque-tag match and MUST NOT accept weak ETags.
    /// "*" always matches any ETag.
    /// </summary>
    /// <param name="left">The first entity-tag (may be "*" or a quoted opaque-tag, optionally with W/ prefix).</param>
    /// <param name="right">The second entity-tag (may be "*" or a quoted opaque-tag, optionally with W/ prefix).</param>
    /// <returns>True if both are "*" or if both are strong ETags with identical opaque-tags.</returns>
    public static bool StrongMatch(string left, string right)
    {
        if (left == "*" || right == "*")
        {
            return true;
        }

        // Reject weak ETags in strong comparison.
        if (IsWeak(left) || IsWeak(right))
        {
            return false;
        }

        return OpaqueTag(left) == OpaqueTag(right);
    }

    /// <summary>
    /// Performs weak entity-tag comparison per RFC 9110 §8.8.3.1.
    /// Weak match ignores W/ prefix and requires identical opaque-tags.
    /// "*" always matches any ETag.
    /// </summary>
    /// <param name="left">The first entity-tag (may be "*" or a quoted opaque-tag, optionally with W/ prefix).</param>
    /// <param name="right">The second entity-tag (may be "*" or a quoted opaque-tag, optionally with W/ prefix).</param>
    /// <returns>True if both are "*" or if both have identical opaque-tags (regardless of weakness).</returns>
    public static bool WeakMatch(string left, string right)
    {
        if (left == "*" || right == "*")
        {
            return true;
        }

        return OpaqueTag(left) == OpaqueTag(right);
    }

    /// <summary>
    /// Determines if the ETag is marked as weak (starts with W/).
    /// </summary>
    public static bool IsWeak(string etag)
    {
        return etag.StartsWith("W/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts the opaque-tag (quoted value) from an ETag, stripping the W/ prefix if present.
    /// Example: "abc123" or W/"abc123" both return "abc123".
    /// </summary>
    private static string OpaqueTag(string etag)
    {
        var tag = etag.StartsWith("W/", StringComparison.Ordinal) ? etag[2..] : etag;
        // Remove surrounding quotes.
        return tag.Trim('"');
    }
}