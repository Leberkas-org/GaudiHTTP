using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Protocol.LineBased;

internal static class RequestLineWriter
{
    public static void Write(ref SpanWriter writer, string methodName, string target, Version version)
    {
        writer.WriteAscii(methodName);
        writer.WriteSpace();
        writer.WriteAscii(target);
        writer.WriteSpace();
        // Write the pre-encoded u8 version bytes directly (matching StatusLineWriter) instead of
        // re-running Encoding.ASCII.GetBytes over the version string on every request.
        writer.WriteBytes(MessageVersionCodec.ToWireBytes(version));
        writer.WriteCrlf();
    }
}