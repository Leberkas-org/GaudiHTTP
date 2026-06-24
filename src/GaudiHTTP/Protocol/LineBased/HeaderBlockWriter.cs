using System.Reflection;
using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Protocol.LineBased;

internal static class HeaderBlockWriter
{
    // Pre-baked ASCII bytes for all well-known header names, keyed by string name (OrdinalIgnoreCase).
    // Populated once at static init from WellKnownHeaders fields — eliminates Encoding.ASCII.GetBytes
    // on every header write for the common case.
    private static readonly Dictionary<string, ReadOnlyMemory<byte>> _wellKnownNameBytes =
        BuildWellKnownNameBytesMap();

    private static Dictionary<string, ReadOnlyMemory<byte>> BuildWellKnownNameBytesMap()
    {
        var map = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in typeof(WellKnownHeaders).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(WellKnownHeader))
            {
                continue;
            }

            var header = (WellKnownHeader)field.GetValue(null)!;
            var name = header.Name;

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            // Only index names that look like HTTP header names (letters, digits, hyphens, underscores).
            // This excludes punctuation-only entries like Colon, Comma, ColonSpace, etc.
            var isHeaderName = true;
            foreach (var ch in name)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '_')
                {
                    isHeaderName = false;
                    break;
                }
            }

            if (!isHeaderName)
            {
                continue;
            }

            map.TryAdd(name, header.Bytes);
        }

        return map;
    }

    public static void Write(ref SpanWriter writer, HeaderCollection headers)
    {
        foreach (var entry in headers)
        {
            if (string.Equals(entry.Name, WellKnownHeaders.Date, StringComparison.OrdinalIgnoreCase))
            {
                // Write the pre-encoded "date: <value>\r\n" bytes directly — no ASCII encoding per response.
                writer.WriteBytes(DateHeaderCache.GetDateHeaderLine());
                continue;
            }

            if (_wellKnownNameBytes.TryGetValue(entry.Name, out var nameBytes))
            {
                writer.WriteBytes(nameBytes.Span);
            }
            else
            {
                writer.WriteAscii(entry.Name);
            }

            writer.WriteColonSpace();
            writer.WriteAscii(SanitizeHeaderValue(entry.Value));
            writer.WriteCrlf();
        }

        writer.WriteCrlf();
    }

    private static string SanitizeHeaderValue(string value)
    {
        if (value.IndexOf('\r') < 0)
        {
            return value;
        }

        return string.Create(value.Length, value, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                if (src[i] == '\r' && (i + 1 >= src.Length || src[i + 1] != '\n'))
                {
                    span[i] = ' ';
                }
                else
                {
                    span[i] = src[i];
                }
            }
        });
    }
}