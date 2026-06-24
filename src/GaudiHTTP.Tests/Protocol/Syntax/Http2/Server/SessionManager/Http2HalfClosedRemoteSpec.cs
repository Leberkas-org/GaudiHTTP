using System.Buffers.Binary;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.SessionManager;

/// <summary>
/// RFC 9113 §5.1: once a peer sends END_STREAM the stream is half-closed(remote). Any subsequent
/// DATA frame on that stream MUST be treated as a STREAM_CLOSED stream error. Previously the server
/// tracked neither IsRemoteClosed nor MarkRemoteClosed, so trailing DATA was silently dropped or
/// fed into an already-completed request body.
/// </summary>
public sealed class Http2HalfClosedRemoteSpec
{
    private static byte[] BuildHeadersFrame(int streamId, bool endStream)
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new(":method", endStream ? "GET" : "POST"),
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
        frame[4] = (byte)(0x04 | (endStream ? 0x01 : 0x00)); // END_HEADERS | (END_STREAM)
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId);
        block.Span.CopyTo(frame.AsSpan(h));
        return frame;
    }

    private static byte[] BuildDataFrame(int streamId, int dataLength, bool endStream)
    {
        const int h = 9;
        var frame = new byte[h + dataLength];
        frame[0] = (byte)(dataLength >> 16);
        frame[1] = (byte)(dataLength >> 8);
        frame[2] = (byte)dataLength;
        frame[3] = (byte)FrameType.Data;
        frame[4] = endStream ? (byte)0x01 : (byte)0x00;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId);
        return frame;
    }

    private static TransportBuffer WrapFrame(byte[] frame)
    {
        var buffer = TransportBuffer.Rent(frame.Length);
        frame.CopyTo(buffer.FullMemory.Span);
        buffer.Length = frame.Length;
        return buffer;
    }

    private static Http2ServerSessionManager CreateSm(FakeServerOps ops)
    {
        var sm = new Http2ServerSessionManager(new TurboServerOptions().ToHttp2Options(), ops);
        sm.PreStart();
        ops.Outbound.Clear();
        return sm;
    }

    private static bool EmittedRstStream(FakeServerOps ops, Http2ErrorCode expected)
    {
        foreach (var outbound in ops.Outbound)
        {
            if (outbound is TransportData { Buffer.Length: >= 13 } td)
            {
                var span = td.Buffer.FullMemory.Span;
                if ((FrameType)span[3] == FrameType.RstStream &&
                    BinaryPrimitives.ReadUInt32BigEndian(span[9..]) == (uint)expected)
                {
                    return true;
                }
            }
        }

        return false;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Data_after_headers_end_stream_should_be_rejected_with_stream_closed()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);

        sm.DecodeClientData(WrapFrame(BuildHeadersFrame(1, endStream: true)));
        Assert.Single(ops.Requests);
        ops.Outbound.Clear();

        sm.DecodeClientData(WrapFrame(BuildDataFrame(1, dataLength: 3, endStream: false)));

        Assert.True(EmittedRstStream(ops, Http2ErrorCode.StreamClosed),
            "DATA after HEADERS(END_STREAM) must be answered with RST_STREAM(STREAM_CLOSED).");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Data_after_data_end_stream_should_be_rejected_with_stream_closed()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);

        sm.DecodeClientData(WrapFrame(BuildHeadersFrame(1, endStream: false)));
        sm.DecodeClientData(WrapFrame(BuildDataFrame(1, dataLength: 4, endStream: true)));
        ops.Outbound.Clear();

        // A second DATA after the client's END_STREAM is a STREAM_CLOSED error.
        sm.DecodeClientData(WrapFrame(BuildDataFrame(1, dataLength: 4, endStream: false)));

        Assert.True(EmittedRstStream(ops, Http2ErrorCode.StreamClosed),
            "DATA after DATA(END_STREAM) must be answered with RST_STREAM(STREAM_CLOSED).");
    }
}
