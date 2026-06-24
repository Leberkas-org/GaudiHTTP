using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.Syntax.Http11;

/// <summary>
/// RFC 9112 §7.1.1: Validates chunk-ext syntax.
/// chunk-ext = *( BWS ";" BWS chunk-ext-name [ BWS "=" BWS chunk-ext-val ] )
/// Semantics of extensions are ignored; only syntax is validated.
/// </summary>
internal static class ChunkExtensionParser
{
    internal static bool TryParse(ReadOnlySpan<byte> extBytes)
    {
        if (extBytes.IsEmpty)
        {
            return true;
        }

        var pos = 0;
        while (pos < extBytes.Length)
        {
            SkipBWS(extBytes, ref pos);

            if (!TryParseExtName(extBytes, ref pos))
            {
                return false;
            }

            SkipBWS(extBytes, ref pos);

            if (pos < extBytes.Length && extBytes[pos] == '=')
            {
                pos++;
                SkipBWS(extBytes, ref pos);

                if (!TryParseExtValue(extBytes, ref pos))
                {
                    return false;
                }
            }

            SkipBWS(extBytes, ref pos);

            if (!TryAdvanceSemicolon(extBytes, ref pos))
            {
                return false;
            }
        }

        return true;
    }

    private static void SkipBWS(ReadOnlySpan<byte> data, ref int pos)
    {
        while (pos < data.Length && (data[pos] == ' ' || data[pos] == '\t'))
        {
            pos++;
        }
    }

    private static bool TryParseExtName(ReadOnlySpan<byte> data, ref int pos)
    {
        var start = pos;
        while (pos < data.Length && IsTokenChar(data[pos]) && data[pos] != ';')
        {
            pos++;
        }

        return pos > start;
    }

    private static bool TryParseExtValue(ReadOnlySpan<byte> data, ref int pos)
    {
        if (pos < data.Length && data[pos] == '"')
        {
            return TryParseQuotedString(data, ref pos);
        }

        var start = pos;
        while (pos < data.Length && (IsTokenChar(data[pos]) || data[pos] == ' ' || data[pos] == '\t') && data[pos] != ';')
        {
            pos++;
        }

        return pos > start;
    }

    private static bool TryParseQuotedString(ReadOnlySpan<byte> data, ref int pos)
    {
        pos++; // consume opening '"'
        while (pos < data.Length && data[pos] != '"')
        {
            if (data[pos] == '\\')
            {
                pos += 2;
            }
            else
            {
                pos++;
            }
        }

        if (pos >= data.Length)
        {
            return false;
        }

        pos++; // consume closing '"'
        return true;
    }

    private static bool TryAdvanceSemicolon(ReadOnlySpan<byte> data, ref int pos)
    {
        if (pos < data.Length && data[pos] == ';')
        {
            pos++;
            return true;
        }

        return pos >= data.Length;
    }

    private static bool IsTokenChar(byte b) => HeaderValidation.IsTokenChar(b);
}
