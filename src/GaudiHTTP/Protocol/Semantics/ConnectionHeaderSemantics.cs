namespace GaudiHTTP.Protocol.Semantics;

/// <summary>
/// RFC 9110 §7.6.1: Connection Header Field
/// Parses and analyzes the Connection header field values for connection control options.
/// </summary>
internal static class ConnectionHeaderSemantics
{
    private static readonly bool[] TokenCharTable = CreateTokenCharTable();

    private static bool[] CreateTokenCharTable()
    {
        var table = new bool[128];

        for (var c = '0'; c <= '9'; c++)
        {
            table[c] = true;
        }

        for (var c = 'A'; c <= 'Z'; c++)
        {
            table[c] = true;
        }

        for (var c = 'a'; c <= 'z'; c++)
        {
            table[c] = true;
        }

        foreach (var c in "!#$%&'*+-.^_`|~")
        {
            table[c] = true;
        }

        return table;
    }

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

    // RFC 9110 §5.1: A token is a sequence of characters with specific restrictions.
    // ALPHA / DIGIT / "!" / "#" / "$" / "%" / "&" / "'" / "*" / "+" / "-" / "." / "^" / "_" / "`" / "|" / "~"
    private static bool IsValidToken(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c >= 128 || !TokenCharTable[c])
            {
                return false;
            }
        }

        return true;
    }
}
