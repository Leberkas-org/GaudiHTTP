using Microsoft.Extensions.Time.Testing;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server.SessionManager;

public sealed class Http2DataRateViolationSpec
{
    private static byte[] BuildHeadersFrame(int streamId, bool endStream)
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

        const int h = 9;
        var frame = new byte[h + written];
        frame[0] = (byte)(written >> 16);
        frame[1] = (byte)(written >> 8);
        frame[2] = (byte)written;
        frame[3] = (byte)FrameType.Headers;
        byte flags = 0x04; // END_HEADERS
        if (endStream)
        {
            flags |= 0x01;
        }

        frame[4] = flags;
        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;
        buf.AsSpan(0, written).CopyTo(frame.AsSpan(h));
        return frame;
    }

    private static byte[] BuildDataFrame(int streamId, int dataLength)
    {
        const int h = 9;
        var frame = new byte[h + dataLength];
        frame[0] = (byte)(dataLength >> 16);
        frame[1] = (byte)(dataLength >> 8);
        frame[2] = (byte)dataLength;
        frame[3] = (byte)FrameType.Data;
        frame[4] = 0; // no END_STREAM — body keeps flowing
        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;
        return frame;
    }

    private static TransportBuffer WrapFrame(byte[] frame)
    {
        var buffer = TransportBuffer.Rent(frame.Length);
        frame.CopyTo(buffer.FullMemory.Span);
        buffer.Length = frame.Length;
        return buffer;
    }

    private static bool HasRstStream(FakeServerOps ops)
    {
        foreach (var outbound in ops.Outbound)
        {
            if (outbound is TransportData { Buffer.Length: >= 9 } td
                && (FrameType)td.Buffer.FullMemory.Span[3] == FrameType.RstStream)
            {
                return true;
            }
        }

        return false;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Slow_request_body_should_emit_rst_stream_after_grace_with_injected_clock()
    {
        var clock = new FakeTimeProvider();
        var ops = new FakeServerOps();
        var options = new GaudiServerOptions
        {
            Http2 = { MinRequestBodyDataRate = 1000, MinRequestBodyDataRateGracePeriod = TimeSpan.FromSeconds(1) }
        }.ToHttp2Options();
        var sm = new Http2ServerSessionManager(options, ops, clock);

        sm.PreStart();
        ops.Outbound.Clear();

        // Open stream 1 (POST, body to follow) and deliver a tiny DATA frame, then stall.
        sm.DecodeClientData(WrapFrame(BuildHeadersFrame(1, endStream: false)));
        sm.DecodeClientData(WrapFrame(BuildDataFrame(1, dataLength: 5)));

        clock.Advance(TimeSpan.FromMilliseconds(600));
        sm.CheckDataRates();
        Assert.False(HasRstStream(ops), "Should be within grace period at first check");

        // 5 bytes over 1700ms = ~2.9 bytes/sec << 1000; grace (1s) expired → RST_STREAM.
        clock.Advance(TimeSpan.FromMilliseconds(1100));
        sm.CheckDataRates();
        Assert.True(HasRstStream(ops), "Expected RST_STREAM after request-body rate violation");
    }
}
