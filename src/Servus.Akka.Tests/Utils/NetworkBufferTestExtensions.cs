using Servus.Akka.IO;

namespace Servus.Akka.Tests.Utils;

public static class NetworkBufferTestExtensions
{
    internal static NetworkBuffer FromArray(byte[] data, int length = -1)
    {
        var len = length < 0 ? data.Length : length;
        var buf = NetworkBuffer.Rent(len);
        data.AsSpan(0, len).CopyTo(buf.FullMemory.Span);
        buf.Length = len;
        return buf;
    }
}