using System.Buffers;
using System.Net;
using GaudiHTTP.Tests.Shared;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Stages;

internal static class Http2ConnectionTestHelper
{
    public static byte[] SerializeFrames(params Http2Frame[] frames)
    {
        var totalSize = 0;
        foreach (var f in frames)
        {
            totalSize += f.SerializedSize;
        }

        var bytes = new byte[totalSize];
        var span = bytes.AsSpan();
        foreach (var f in frames)
        {
            f.WriteTo(ref span);
        }

        return bytes;
    }

    public static InMemoryTransport CreateTransportWithFrames(params Http2Frame[] frames)
    {
        var transport = new InMemoryTransport();
        if (frames.Length > 0)
        {
            transport.Feed(SerializeFrames(frames));
        }

        return transport;
    }

    public static TransportConnected CreateTransportConnected(InMemoryTransport transport)
        => new(new ConnectionInfo(
            new IPEndPoint(IPAddress.Loopback, 0),
            new IPEndPoint(IPAddress.Loopback, 443),
            TransportProtocol.Tcp), transport);

    public static IReadOnlyList<Http2Frame> DecodeFrames(ReadOnlyMemory<byte> data,
        bool skipPreface = false)
    {
        if (data.Length == 0)
        {
            return [];
        }

        const int prefaceMagicLength = 24;
        if (data.Length <= prefaceMagicLength)
        {
            return [];
        }

        var decoder = new FrameDecoder();
        var afterMagic = new ReadOnlySequence<byte>(data[prefaceMagicLength..]);
        var allFrames = decoder.DecodeAll(afterMagic, out _).ToArray();

        var startIndex = 0;
        if (allFrames.Length > 0 && allFrames[0] is SettingsFrame)
        {
            startIndex = 1;
        }

        if (skipPreface)
        {
            while (startIndex < allFrames.Length && allFrames[startIndex] is SettingsFrame)
            {
                startIndex++;
            }
        }

        if (startIndex >= allFrames.Length)
        {
            return [];
        }

        return allFrames[startIndex..];
    }
}
