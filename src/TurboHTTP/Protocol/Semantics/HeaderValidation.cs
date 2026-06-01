namespace TurboHTTP.Protocol.Semantics;

internal static class HeaderValidation
{
    // RFC 9110 §5.6.2: tchar = "!" / "#" / "$" / "%" / "&" / "'" / "*" / "+"
    //                / "-" / "." / "^" / "_" / "`" / "|" / "~" / DIGIT / ALPHA
    public static bool IsToken(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            if (!IsTokenChar((char)value[i]))
            {
                return false;
            }
        }

        return true;
    }

    // RFC 9110 §5.5: field-value = *( field-content )
    //   field-content  = field-vchar [ 1*( SP / HTAB / field-vchar ) field-vchar ]
    //   field-vchar    = VCHAR / obs-text
    //   obs-text       = %x80-FF
    public static bool IsValidFieldValue(ReadOnlySpan<byte> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var b = value[i];
            if (b == ' ' || b == '\t')
            {
                continue;
            }

            if (b is >= 0x21 and <= 0x7E)
            {
                continue;
            }

            if (b >= 0x80)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    public static string TrimOws(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsOws(value[start]))
        {
            start++;
        }

        while (end > start && IsOws(value[end - 1]))
        {
            end--;
        }

        return start == 0 && end == value.Length ? value : value[start..end];
    }

    public static bool IsTokenChar(byte b)
    {
        return b switch
        {
            >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' => true,
            _ => b is (byte)'!' or (byte)'#' or (byte)'$' or (byte)'%' or (byte)'&' or (byte)'\''
                or (byte)'*' or (byte)'+' or (byte)'-' or (byte)'.' or (byte)'^' or (byte)'_'
                or (byte)'`' or (byte)'|' or (byte)'~'
        };
    }

    private static bool IsTokenChar(char c)
    {
        return c switch
        {
            >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' => true,
            _ => c is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|'
                or '~'
        };
    }

    private static bool IsOws(char c) => c is ' ' or '\t';
}