using System.Buffers;
using System.Net;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Stages;

internal static class Http2ConnectionTestHelper
{
    public static ITransportInbound FramesToInput(params Http2Frame[] frames)
    {
        var totalSize = 0;
        foreach (var f in frames)
        {
            totalSize += f.SerializedSize;
        }

        var buf = WireBuffer.Rent(totalSize);
        var span = buf.FullMemory.Span;
        foreach (var f in frames)
        {
            f.WriteTo(ref span);
        }

        buf.Length = totalSize;
        return TransportData.Rent(buf);
    }

    public static IEnumerable<ITransportInbound> FramesToInputs(IEnumerable<Http2Frame> frames)
    {
        foreach (var f in frames)
        {
            yield return FramesToInput(f);
        }
    }

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

    public static IReadOnlyList<Http2Frame> DecodeFrames(IEnumerable<ITransportOutbound> items,
        bool skipPreface = false)
    {
        var decoder = new FrameDecoder();
        var result = new List<Http2Frame>();
        var skippedPrefaceData = false;
        foreach (var item in items)
        {
            if (item is TransportData { Buffer: var buffer })
            {
                if (skipPreface && !skippedPrefaceData)
                {
                    skippedPrefaceData = true;
                    continue;
                }

                var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(buffer.Memory), out _);
                result.AddRange(frames);
            }
        }

        return result;
    }

    /// <summary>
    /// Decodes H2 frames from transport output bytes. The transport always contains
    /// the client preface (24-byte magic + initial SETTINGS frame) followed by any
    /// response frames (WINDOW_UPDATE, PING ACK, HEADERS, etc.).
    /// The 24-byte preface magic and the initial client SETTINGS frame are always
    /// stripped so callers only see response frames.
    /// When <paramref name="skipPreface"/> is true, all frames before the first
    /// non-SETTINGS frame are also excluded (for tests that want HEADERS only).
    /// </summary>
    public static IReadOnlyList<Http2Frame> DecodeFrames(ReadOnlyMemory<byte> data,
        bool skipPreface = false)
    {
        if (data.Length == 0)
        {
            return [];
        }

        // The client always writes the 24-byte preface magic followed by a SETTINGS frame.
        const int prefaceMagicLength = 24;
        if (data.Length <= prefaceMagicLength)
        {
            return [];
        }

        var decoder = new FrameDecoder();
        var afterMagic = new ReadOnlySequence<byte>(data[prefaceMagicLength..]);
        var allFrames = decoder.DecodeAll(afterMagic, out _).ToArray();

        // The first frame is always the client SETTINGS (part of the preface). Skip it.
        // When skipPreface is true, also skip any additional SETTINGS frames that follow
        // (e.g., SETTINGS ACK emitted in response to server SETTINGS).
        var startIndex = 0;
        if (allFrames.Length > 0 && allFrames[0] is SettingsFrame)
        {
            startIndex = 1;
        }

        if (skipPreface)
        {
            // Skip all leading SETTINGS frames
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

    public static IReadOnlyList<ITransportOutbound> ExtractSignals(IEnumerable<ITransportOutbound> items)
    {
        var result = new List<ITransportOutbound>();
        foreach (var item in items)
        {
            if (item is not TransportData)
            {
                result.Add(item);
            }
        }

        return result;
    }
}
