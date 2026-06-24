namespace TurboHTTP.Protocol.Semantics;

/// <summary>
/// A byte range as parsed from a Range header per RFC 9110 §14.1.1.
/// </summary>
internal readonly record struct ByteRange(long? Start, long? End, long? SuffixLength)
{
    /// <summary>
    /// Returns true if this represents a suffix-range (e.g., "bytes=-500").
    /// </summary>
    public bool IsSuffixRange => Start is null && SuffixLength is not null;

    /// <summary>
    /// Returns true if this represents an open-ended range (e.g., "bytes=500-").
    /// </summary>
    public bool IsOpenEnded => End is null && Start is not null;
}

/// <summary>
/// A Content-Range value as parsed from a Content-Range header per RFC 9110 §14.4.
/// </summary>
internal readonly record struct ContentRangeValue(long? Start, long? End, long? CompleteLength, bool IsUnsatisfied)
{
    /// <summary>
    /// Returns true if the Content-Range represents an unsatisfied range (e.g., "bytes */1000").
    /// </summary>
    public bool IsValid => !IsUnsatisfied;
}

/// <summary>
/// RFC 9110 §14 — Parses Range and Content-Range headers for HTTP byte ranges.
/// Supports single ranges, multiple ranges, suffix ranges, open-ended ranges,
/// and Content-Range responses (including unsatisfied ranges).
/// </summary>
internal static class RangeParser
{
    /// <summary>
    /// Parses a Range header value per RFC 9110 §14.1.
    /// Supports: "bytes=0-499", "bytes=-500", "bytes=500-", "bytes=0-499,500-999"
    /// Returns an empty list if the header is null, not "bytes" range, or syntactically invalid.
    /// </summary>
    /// <param name="rangeHeader">The Range header value, or null if absent.</param>
    /// <returns>A list of ByteRange structs. Empty if invalid or non-bytes.</returns>
    public static IReadOnlyList<ByteRange> Parse(string? rangeHeader)
    {
        if (string.IsNullOrWhiteSpace(rangeHeader))
        {
            return [];
        }

        var trimmed = rangeHeader.Trim();
        if (!trimmed.StartsWith("bytes=", StringComparison.Ordinal))
        {
            return [];
        }

        var rangeSpec = trimmed[6..];
        var ranges = new List<ByteRange>();
        var rangeParts = rangeSpec.Split(',');

        foreach (var part in rangeParts)
        {
            var rangePart = part.Trim();
            if (string.IsNullOrEmpty(rangePart))
            {
                continue;
            }

            if (TryParseRange(rangePart, out var range))
            {
                ranges.Add(range);
            }
            else
            {
                // Invalid range syntax — return empty list.
                return [];
            }
        }

        return ranges.Count > 0 ? ranges.AsReadOnly() : [];
    }

    /// <summary>
    /// Parses a Content-Range header value per RFC 9110 §14.4.
    /// Supports: "bytes 0-499/1000" (satisfied), "bytes */1000" (unsatisfied), "bytes 0-499/*" (unknown total).
    /// Returns null if the header is absent or syntactically invalid.
    /// </summary>
    /// <param name="contentRangeHeader">The Content-Range header value, or null if absent.</param>
    /// <returns>A ContentRangeValue struct, or null if invalid.</returns>
    public static ContentRangeValue? ParseContentRange(string? contentRangeHeader)
    {
        if (string.IsNullOrWhiteSpace(contentRangeHeader))
        {
            return null;
        }

        var trimmed = contentRangeHeader.Trim();
        if (!trimmed.StartsWith("bytes ", StringComparison.Ordinal))
        {
            return null;
        }

        var rangeSpec = trimmed[6..].Trim();

        // Unsatisfied range: "bytes */total"
        if (rangeSpec.StartsWith("*/", StringComparison.Ordinal))
        {
            var unsatisfiedTotal = rangeSpec[2..];
            if (long.TryParse(unsatisfiedTotal, out var completeLength))
            {
                return new ContentRangeValue(null, null, completeLength, IsUnsatisfied: true);
            }

            return null;
        }

        // Satisfied range: "bytes start-end/total" or "bytes start-end/*"
        var parts = rangeSpec.Split('/');
        if (parts.Length != 2)
        {
            return null;
        }

        var rangePart = parts[0].Trim();
        var contentRangeTotal = parts[1].Trim();

        var rangeDash = rangePart.Split('-');
        if (rangeDash.Length != 2)
        {
            return null;
        }

        if (!long.TryParse(rangeDash[0], out var start) || !long.TryParse(rangeDash[1], out var end))
        {
            return null;
        }

        long? completeLen = null;
        if (contentRangeTotal != "*" && long.TryParse(contentRangeTotal, out var tmpLen))
        {
            completeLen = tmpLen;
        }
        else if (contentRangeTotal != "*")
        {
            return null;
        }

        return new ContentRangeValue(start, end, completeLen, IsUnsatisfied: false);
    }

    /// <summary>
    /// Attempts to parse a single range part (e.g., "0-499", "-500", "500-").
    /// </summary>
    private static bool TryParseRange(string rangePart, out ByteRange range)
    {
        range = default;

        if (rangePart.StartsWith('-'))
        {
            // Suffix range: "-500"
            var suffix = rangePart[1..];
            if (long.TryParse(suffix, out var suffixLength) && suffixLength > 0)
            {
                range = new ByteRange(null, null, suffixLength);
                return true;
            }

            return false;
        }

        var parts = rangePart.Split('-');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!long.TryParse(parts[0], out var start) || start < 0)
        {
            return false;
        }

        // Open-ended range: "500-"
        if (string.IsNullOrEmpty(parts[1]))
        {
            range = new ByteRange(start, null, null);
            return true;
        }

        // Normal range: "0-499"
        if (long.TryParse(parts[1], out var end) && end >= start)
        {
            range = new ByteRange(start, end, null);
            return true;
        }

        return false;
    }
}
