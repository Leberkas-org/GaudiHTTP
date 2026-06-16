using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.LineBased;

internal static class HeaderBlockWriter
{
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

            writer.WriteAscii(entry.Name);
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