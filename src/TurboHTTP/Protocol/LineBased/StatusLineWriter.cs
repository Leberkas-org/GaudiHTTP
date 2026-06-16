using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.LineBased;

internal static class StatusLineWriter
{
    public static void Write(ref SpanWriter writer, Version version, int statusCode, string? reason = null)
    {
        writer.WriteBytes(MessageVersionCodec.ToWireBytes(version));
        writer.WriteSpace();
        writer.WriteStatusCode(statusCode);
        writer.WriteSpace();

        if (reason is null)
        {
            writer.WriteBytes(ReasonPhrases.ForBytes(statusCode));
        }
        else
        {
            writer.WriteAscii(reason);
        }

        writer.WriteCrlf();
    }
}