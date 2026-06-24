using System.Net;
using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Protocol.LineBased;

internal static class StatusLineWriter
{
    public static void Write(ref SpanWriter writer, Version version, int statusCode, string? reason = null)
    {
        if (reason is null)
        {
            if (version.Equals(HttpVersion.Version11) && PreBakedStatusLines.TryGetHttp11(statusCode, out var line11))
            {
                writer.WriteBytes(line11.Span);
                return;
            }

            if (version.Equals(HttpVersion.Version10) && PreBakedStatusLines.TryGetHttp10(statusCode, out var line10))
            {
                writer.WriteBytes(line10.Span);
                return;
            }
        }

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
