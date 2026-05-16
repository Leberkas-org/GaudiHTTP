using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.LineBased;

internal static class StatusLineWriter
{
    public static void Write(ref SpanWriter writer, Version version, int statusCode, string? reason = null)
    {
        var versionStr = MessageVersionCodec.ToWireFormat(version);
        reason ??= ReasonPhrases.For(statusCode);

        writer.WriteAscii(versionStr);
        writer.WriteSpace();
        writer.WriteStatusCode(statusCode);
        writer.WriteSpace();
        writer.WriteAscii(reason);
        writer.WriteCrlf();
    }
}