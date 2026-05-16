using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.LineBased;

internal static class HeaderBlockWriter
{
    public static void Write(ref SpanWriter writer, HeaderCollection headers)
    {
        foreach (var entry in headers)
        {
            writer.WriteAscii(entry.Name);
            writer.WriteColonSpace();
            writer.WriteAscii(entry.Value);
            writer.WriteCrlf();
        }

        writer.WriteCrlf();
    }
}