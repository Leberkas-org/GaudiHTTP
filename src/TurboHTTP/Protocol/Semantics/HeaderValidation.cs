namespace TurboHTTP.Protocol.Semantics;

internal static class HeaderValidation
{
    // RFC 9110 §5.6.2: tchar = "!" / "#" / "$" / "%" / "&" / "'" / "*" / "+"
    //                / "-" / "." / "^" / "_" / "`" / "|" / "~" / DIGIT / ALPHA
    private static readonly bool[] TokenCharTable = CreateTokenCharTable();

    // RFC 9110 §5.5: field-value valid bytes — SP, HTAB, VCHAR (%x21-7E), obs-text (%x80-FF)
    private static readonly bool[] FieldValueCharTable = CreateFieldValueCharTable();

    private static bool[] CreateTokenCharTable()
    {
        var table = new bool[256];

        for (var c = (byte)'0'; c <= (byte)'9'; c++)
        {
            table[c] = true;
        }

        for (var c = (byte)'A'; c <= (byte)'Z'; c++)
        {
            table[c] = true;
        }

        for (var c = (byte)'a'; c <= (byte)'z'; c++)
        {
            table[c] = true;
        }

        foreach (var c in "!#$%&'*+-.^_`|~")
        {
            table[(byte)c] = true;
        }

        return table;
    }

    private static bool[] CreateFieldValueCharTable()
    {
        var table = new bool[256];

        table[' '] = true;
        table['\t'] = true;

        for (var b = 0x21; b <= 0x7E; b++)
        {
            table[b] = true;
        }

        for (var b = 0x80; b <= 0xFF; b++)
        {
            table[b] = true;
        }

        return table;
    }

    public static bool IsToken(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            if (!TokenCharTable[value[i]])
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
            if (!FieldValueCharTable[value[i]])
            {
                return false;
            }
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

    public static bool IsTokenChar(byte b) => TokenCharTable[b];

    private static bool IsOws(char c) => c is ' ' or '\t';
}