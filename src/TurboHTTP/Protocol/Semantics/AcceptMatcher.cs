namespace TurboHTTP.Protocol.Semantics;

/// <summary>
/// RFC 9110 §12.5 — Provides content negotiation matching for Accept, Accept-Encoding, and Accept-Language headers.
/// Implements matching semantics for media types, encodings, and language tags.
/// </summary>
internal static class AcceptMatcher
{
    /// <summary>
    /// Matches a media type pattern against an offered media type.
    /// Supports exact matching, type/*, */*, and case-insensitive comparison.
    /// Null or empty pattern matches all media types.
    /// </summary>
    /// <param name="acceptPattern">The pattern from Accept header (e.g., "text/html", "text/*", "*/*"), or null/empty to match all.</param>
    /// <param name="offered">The offered media type (e.g., "text/html").</param>
    /// <returns>True if the offered type matches the pattern.</returns>
    public static bool MatchesMediaType(string? acceptPattern, string offered)
    {
        if (string.IsNullOrWhiteSpace(acceptPattern))
        {
            return true;
        }

        var pattern = acceptPattern.AsSpan().Trim();
        var offeredSpan = offered.AsSpan().Trim();

        if (pattern.Equals("*/*", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var slashIndex = pattern.IndexOf('/');
        if (slashIndex < 0)
        {
            return false;
        }

        var patternType = pattern[..slashIndex];
        var patternSubtype = pattern[(slashIndex + 1)..];

        var offeredSlashIndex = offeredSpan.IndexOf('/');
        if (offeredSlashIndex < 0)
        {
            return false;
        }

        var offeredType = offeredSpan[..offeredSlashIndex];
        var offeredSubtype = offeredSpan[(offeredSlashIndex + 1)..];

        if (!patternType.Equals(offeredType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (patternSubtype.Equals("*", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return patternSubtype.Equals(offeredSubtype, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Matches an encoding pattern against an offered encoding.
    /// "identity" is always acceptable. "*" matches all encodings.
    /// Comparison is case-insensitive.
    /// </summary>
    /// <param name="acceptPattern">The pattern from Accept-Encoding header (e.g., "gzip", "identity", "*").</param>
    /// <param name="offered">The offered encoding (e.g., "gzip").</param>
    /// <returns>True if the offered encoding matches the pattern.</returns>
    public static bool MatchesEncoding(string acceptPattern, string offered)
    {
        if (string.IsNullOrWhiteSpace(acceptPattern) || string.IsNullOrWhiteSpace(offered))
        {
            return false;
        }

        var pattern = acceptPattern.AsSpan().Trim();
        var offeredSpan = offered.AsSpan().Trim();

        if (pattern.Equals("*", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (pattern.Equals(WellKnownHeaders.IdentityValue, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return pattern.Equals(offeredSpan, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Matches a language tag pattern against an offered language tag.
    /// Supports prefix matching (e.g., "en" matches "en-US").
    /// "*" matches all languages. Comparison is case-insensitive.
    /// </summary>
    /// <param name="acceptPattern">The pattern from Accept-Language header (e.g., "en", "en-US", "fr", "*"), or null/empty to match all.</param>
    /// <param name="offered">The offered language tag (e.g., "en-US").</param>
    /// <returns>True if the offered language matches the pattern.</returns>
    public static bool MatchesLanguage(string? acceptPattern, string offered)
    {
        if (string.IsNullOrWhiteSpace(acceptPattern))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(offered))
        {
            return false;
        }

        var pattern = acceptPattern.AsSpan().Trim();
        var offeredSpan = offered.AsSpan().Trim();

        if (pattern.Equals("*", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (pattern.Equals(offeredSpan, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var dashIndex = offeredSpan.IndexOf('-');
        if (dashIndex > 0)
        {
            var offeredPrefix = offeredSpan[..dashIndex];
            if (pattern.Equals(offeredPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}