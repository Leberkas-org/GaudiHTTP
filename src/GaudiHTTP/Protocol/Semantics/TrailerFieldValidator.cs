namespace GaudiHTTP.Protocol.Semantics;

/// <summary>
/// RFC 9110 §6.5-6.6.2: Trailer Fields Validation
/// Validates trailer fields and manages allowed field names in trailer sections.
/// </summary>
internal static class TrailerFieldValidator
{
    private static readonly HashSet<string> RestrictedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnownHeaders.TransferEncoding,
        WellKnownHeaders.ContentEncoding,
        WellKnownHeaders.ContentLength,
        WellKnownHeaders.Connection,
        WellKnownHeaders.KeepAliveValue,
        WellKnownHeaders.Trailer,
        WellKnownHeaders.Te,
        WellKnownHeaders.Upgrade,
        WellKnownHeaders.ProxyAuthenticate,
        WellKnownHeaders.ProxyAuthorization
    };

    /// <summary>
    /// RFC 9110 §6.6.2: Parse Trailer header field value.
    /// Returns a list of field names that are expected in the trailer section.
    /// Field names are comma-separated and case-insensitive.
    /// </summary>
    public static List<string> Parse(string? trailerHeader)
    {
        var fieldNames = new List<string>();

        if (string.IsNullOrWhiteSpace(trailerHeader))
        {
            return fieldNames;
        }

        var parts = trailerHeader.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                fieldNames.Add(trimmed);
            }
        }

        return fieldNames;
    }

    /// <summary>
    /// RFC 9110 §6.5.1: Determines if a field is allowed in the trailer section.
    /// Prohibits hop-by-hop headers and other fields that must be processed before content.
    /// </summary>
    public static bool IsAllowedInTrailer(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        return !RestrictedHeaders.Contains(fieldName);
    }
}