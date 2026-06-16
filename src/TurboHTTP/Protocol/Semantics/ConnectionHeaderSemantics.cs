namespace TurboHTTP.Protocol.Semantics;

/// <summary>
/// RFC 9110 §7.6.1: Connection Header Field
/// Parses and analyzes the Connection header field values for connection control options.
/// </summary>
internal static class ConnectionHeaderSemantics
{
    /// <summary>
    /// RFC 9110 §7.6.1: Parse a Connection header field value.
    /// Returns a list of connection options (comma-separated tokens).
    /// The values are normalized to lowercase for comparison.
    /// </summary>
    public static List<string> Parse(string? headerValue)
    {
        var options = new List<string>();

        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return options;
        }

        var parts = headerValue.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrEmpty(trimmed) && IsValidToken(trimmed))
            {
                options.Add(trimmed.ToLowerInvariant());
            }
        }

        return options;
    }

    /// <summary>
    /// Checks if the "close" option is present in the Connection header field.
    /// </summary>
    public static bool HasCloseOption(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        var parts = headerValue.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.Equals(trimmed, WellKnownHeaders.CloseValue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the given token is present in a comma-separated header field value
    /// (case-insensitive). Used for Upgrade header scanning without allocating a list.
    /// </summary>
    public static bool HasToken(string? headerValue, string token)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        var parts = headerValue.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.Equals(trimmed, token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the "upgrade" option is present in the Connection header field.
    /// </summary>
    public static bool HasUpgradeOption(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        var parts = headerValue.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.Equals(trimmed, WellKnownHeaders.Upgrade, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// RFC 9110 §5.1: A token is a sequence of characters with specific restrictions.
    /// Validates that a string contains only valid token characters (ALPHA / DIGIT / "!" / "#" / "$" / "%" / "&" / "'" / "*" / "+" / "-" / "." / "^" / "_" / "`" / "|" / "~").
    /// </summary>
    private static bool IsValidToken(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!IsTokenChar(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTokenChar(char c)
    {
        return char.IsLetterOrDigit(c) ||
               c == '!' || c == '#' || c == '$' || c == '%' || c == '&' ||
               c == '\'' || c == '*' || c == '+' || c == '-' || c == '.' ||
               c == '^' || c == '_' || c == '`' || c == '|' || c == '~';
    }
}
