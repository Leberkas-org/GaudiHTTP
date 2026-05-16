using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.LineBased;

internal static class RequestLineWriter
{
    public static void Write(ref SpanWriter writer, string methodName, string target, Version version)
    {
        var versionStr = MessageVersionCodec.ToWireFormat(version);

        writer.WriteAscii(methodName);
        writer.WriteSpace();
        writer.WriteAscii(target);
        writer.WriteSpace();
        writer.WriteAscii(versionStr);
        writer.WriteCrlf();
    }
}