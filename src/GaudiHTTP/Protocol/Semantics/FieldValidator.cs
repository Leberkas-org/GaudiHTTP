namespace GaudiHTTP.Protocol.Semantics;

internal static class FieldValidator
{
    private static readonly bool[] IsTokenChar = CreateTokenCharTable();

    private static bool[] CreateTokenCharTable()
    {
        var table = new bool[128];

        for (var c = '0'; c <= '9'; c++)
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

    public static void Validate<T>(
        IReadOnlyList<T> headers,
        Func<T, string> getName,
        Func<T, string> getValue,
        string uppercaseSection,
        string tokenSection,
        string fieldValueSection,
        string connectionSection)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var name = getName(headers[i]);

            if (name.Length > 0 && name[0] == ':')
            {
                continue;
            }

            ValidateFieldName(name, uppercaseSection, tokenSection);
            ValidateFieldValue(name, getValue(headers[i]), fieldValueSection);
            ValidateConnectionSpecific(name, getValue(headers[i]), connectionSection);
        }
    }

    public static void ValidateFieldName(string name, string uppercaseSection, string tokenSection)
    {
        if (name.Length == 0)
        {
            throw new HttpProtocolException(
                string.Concat(tokenSection, ": Empty field name is not a valid token"));
        }

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (c is >= 'A' and <= 'Z')
            {
                throw new HttpProtocolException(
                    $"{uppercaseSection}: Field name '{name}' contains uppercase character '{c}' at position {i}");
            }

            if (c >= 128 || !IsTokenChar[c])
            {
                throw new HttpProtocolException(
                    $"{tokenSection}: Field name '{name}' contains invalid character 0x{(int)c:X2} at position {i}");
            }
        }
    }

    public static void ValidateFieldValue(string name, string value, string rfcSection)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            switch (c)
            {
                case '\0':
                    throw new HttpProtocolException(
                        $"{rfcSection}: Field '{name}' value contains NUL (0x00) at position {i}");
                case '\r':
                    throw new HttpProtocolException(
                        $"{rfcSection}: Field '{name}' value contains CR (0x0D) at position {i}");
                case '\n':
                    throw new HttpProtocolException(
                        $"{rfcSection}: Field '{name}' value contains LF (0x0A) at position {i}");
            }
        }
    }

    public static void ValidateConnectionSpecific(string name, string value, string rfcSection)
    {
        if (ContentHeaderClassifier.TryGetForbiddenCanonicalName(name, out var canonicalName))
        {
            throw new HttpProtocolException(
                string.Concat(rfcSection, ": ", canonicalName, " header is forbidden"));
        }

        if (string.Equals(name, WellKnownHeaders.Te, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, WellKnownHeaders.Trailers, StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpProtocolException(
                $"{rfcSection}: TE header is only allowed with value 'trailers', got '{value}'");
        }
    }
}
