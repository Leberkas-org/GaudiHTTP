namespace TurboHTTP.Protocol.Semantics;

/// <summary>
/// RFC 9110 §8.4: Content-Encoding Support
/// Determines which content codings are supported by the HTTP implementation.
/// </summary>
internal static class ContentEncodingSupport
{
    private static readonly string[] SupportedCodings = ["gzip", "deflate", "br", "identity"];
    private static readonly IReadOnlyList<string> SupportedCodingsList = SupportedCodings.AsReadOnly();

    /// <summary>
    /// Determines if a content coding is supported by the implementation.
    /// Supports: gzip, deflate, br (Brotli), identity (and legacy x-gzip alias).
    /// </summary>
    public static bool IsSupported(string? encoding)
    {
        if (string.IsNullOrWhiteSpace(encoding))
        {
            return false;
        }

        var normalized = encoding.Trim().ToLowerInvariant();

        return normalized switch
        {
            "gzip" or "x-gzip" or "deflate" or "br" or "identity" => true,
            _ => false
        };
    }

    /// <summary>
    /// Returns the list of supported content codings.
    /// </summary>
    public static IReadOnlyList<string> GetSupportedCodings()
    {
        return SupportedCodingsList;
    }
}