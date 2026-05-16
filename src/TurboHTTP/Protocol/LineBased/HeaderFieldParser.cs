using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.LineBased;

internal static class HeaderFieldParser
{
    public static bool TryParse(ReadOnlySpan<byte> line, out string name, out string value)
    {
        name = null!;
        value = null!;

        if (line.IsEmpty)
        {
            return false;
        }

        if (line[0] == (byte)' ' || line[0] == (byte)'\t')
        {
            return false;
        }

        var colon = line.IndexOf(WellKnownHeaders.Colon);
        if (colon <= 0)
        {
            return false;
        }

        var nameSpan = line[..colon];
        for (var i = 0; i < nameSpan.Length; i++)
        {
            if (nameSpan[i] == (byte)' ' || nameSpan[i] == (byte)'\t')
            {
                return false;
            }
        }

        if (!HeaderValidation.IsToken(nameSpan))
        {
            return false;
        }

        var valueStart = BufferSearch.SkipOws(line, colon + 1);
        var valueEnd = line.Length;
        while (valueEnd > valueStart && (line[valueEnd - 1] == (byte)' ' || line[valueEnd - 1] == (byte)'\t'))
        {
            valueEnd--;
        }

        var valueSpan = valueEnd <= valueStart ? ReadOnlySpan<byte>.Empty : line[valueStart..valueEnd];

        if (!HeaderValidation.IsValidFieldValue(valueSpan))
        {
            return false;
        }

        name = WellKnownHeaders.GetOrCreateHeaderNameIgnoreCase(nameSpan).Name;
        value = valueSpan.IsEmpty ? string.Empty : WellKnownHeaders.GetOrCreateHeaderValue(valueSpan).Name;
        return true;
    }
}
