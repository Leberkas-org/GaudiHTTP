using System.Buffers.Binary;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server.SessionManager;

/// <summary>
/// RFC 9113 §6.1: a padded DATA frame's full payload (Pad Length octet + data + padding) is
/// counted against the receive window. With a tiny stream window, a padded frame whose data alone
/// fits but whose full payload overflows must be rejected with a stream FLOW_CONTROL_ERROR — proof
/// the server credits flow control by the full padded length, not just the application data.
/// </summary>
public sealed class Http2PaddedDataFlowControlSpec
{
    private static byte[] BuildHeadersFrame(int streamId)
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new(":method", "POST"),
            new(":path", "/"),
            new(":scheme", "https"),
            new(":authority", "localhost"),
        };
        var buf = new byte[4096];
        var span = buf.AsSpan();
        var written = encoder.Encode(headers, ref span, useHuffman: false);
        var block = new Memory<byte>(buf, 0, written);

        const int h = 9;
        var frame = new byte[h + block.Length];
        frame[0] = (byte)(block.Length >> 16);
        frame[1] = (byte)(block.Length >> 8);
        frame[2] = (byte)block.Length;
        frame[3] = (byte)FrameType.Headers;
        frame[4] = 0x04; // END_HEADERS, not END_STREAM (body follows)
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId);
        block.Span.CopyTo(frame.AsSpan(h));
        return frame;
    }

    private static byte[] BuildPaddedDataFrame(int streamId, byte[] data, int paddingLength)
    {
        var payloadLength = 1 + data.Length + paddingLength;
        var frame = new byte[9 + payloadLength];
        frame[0] = (byte)(payloadLength >> 16);
        frame[1] = (byte)(payloadLength >> 8);
        frame[2] = (byte)payloadLength;
        frame[3] = (byte)FrameType.Data;
        frame[4] = 0x08; // PADDED
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId);
        frame[9] = (byte)paddingLength;
        Array.Copy(data, 0, frame, 10, data.Length);
        return frame;
    }

    private static TransportBuffer WrapFrame(byte[] frame)
    {
        var buffer = TransportBuffer.Rent(frame.Length);
        frame.CopyTo(buffer.FullMemory.Span);
        buffer.Length = frame.Length;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Padded_data_should_count_full_payload_against_stream_flow_control()
    {
        var ops = new FakeServerOps();
        var baseOptions = new GaudiServerOptions
        {
            Http2 =
            {
                InitialStreamWindowSize = 100,
                EnableAdaptiveWindowScaling = false
            }
        };
        var sm = new Http2ServerSessionManager(baseOptions.ToHttp2Options(), ops);

        sm.PreStart();
        ops.Outbound.Clear();

        sm.DecodeClientData(WrapFrame(BuildHeadersFrame(1)));
        ops.Outbound.Clear();

        // 10 data + 1 Pad Length octet + 250 padding = 261 full payload. The data alone (10)
        // fits the 100-byte window; only counting the full payload overflows it.
        var dataFrame = BuildPaddedDataFrame(1, new byte[10], paddingLength: 250);
        sm.DecodeClientData(WrapFrame(dataFrame));

        Assert.True(EmittedRstStreamWithFlowControlError(ops),
            "Padded DATA exceeding the receive window must trigger RST_STREAM(FLOW_CONTROL_ERROR); " +
            "RFC 9113 §6.1 counts the Pad Length octet and padding against flow control.");
    }

    private static bool EmittedRstStreamWithFlowControlError(FakeServerOps ops)
    {
        foreach (var outbound in ops.Outbound)
        {
            if (outbound is TransportData { Buffer.Length: >= 13 } td)
            {
                var span = td.Buffer.FullMemory.Span;
                if ((FrameType)span[3] == FrameType.RstStream &&
                    BinaryPrimitives.ReadUInt32BigEndian(span[9..]) == (uint)Http2ErrorCode.FlowControlError)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
