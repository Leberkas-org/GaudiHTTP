using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server.SessionManager;

public sealed class Http2ServerTrailerSpec
{
    private static byte[] BuildHeadersFrame(int streamId, List<HpackHeader> headers, bool endStream, HpackEncoder encoder)
    {
        var buf = new byte[4096];
        var span = buf.AsSpan();
        var written = encoder.Encode(headers, ref span, useHuffman: false);
        var block = new Memory<byte>(buf, 0, written);

        const int h = 9;
        var frame = new byte[h + block.Length];
        var len = block.Length;
        frame[0] = (byte)(len >> 16);
        frame[1] = (byte)(len >> 8);
        frame[2] = (byte)len;
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
        block.Span.CopyTo(frame.AsSpan(h));
        return frame;
    }

    private static byte[] BuildDataFrame(int streamId, byte[] data, bool endStream)
    {
        const int h = 9;
        var frame = new byte[h + data.Length];
        var len = data.Length;
        frame[0] = (byte)(len >> 16);
        frame[1] = (byte)(len >> 8);
        frame[2] = (byte)len;
        frame[3] = (byte)FrameType.Data;
        frame[4] = endStream ? (byte)0x01 : (byte)0x00;
        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;
        data.CopyTo(frame, h);
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
    [Trait("RFC", "RFC9113-8.1")]
    public void HandleHeadersFrame_should_not_emit_second_request_for_trailers()
    {
        var ops = new FakeServerOps();
        var baseOptions = new GaudiServerOptions();
        var options = baseOptions.ToHttp2Options();
        var encoder = new HpackEncoder(useHuffman: false);
        var sm = new Http2ServerSessionManager(options, ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var requestHeaders = new List<HpackHeader>
        {
            new(":method", "POST"),
            new(":path", "/upload"),
            new(":scheme", "https"),
            new(":authority", "localhost"),
            new("content-type", "application/octet-stream"),
        };
        var headersFrame = BuildHeadersFrame(1, requestHeaders, endStream: false, encoder);
        sm.DecodeClientData(WrapFrame(headersFrame));

        Assert.Single(ops.Requests);

        var dataFrame = BuildDataFrame(1, "hello"u8.ToArray(), endStream: false);
        sm.DecodeClientData(WrapFrame(dataFrame));

        var trailerHeaders = new List<HpackHeader>
        {
            new("x-checksum", "abc123"),
        };
        var trailerFrame = BuildHeadersFrame(1, trailerHeaders, endStream: true, encoder);
        sm.DecodeClientData(WrapFrame(trailerFrame));

        Assert.Single(ops.Requests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task HandleHeadersFrame_should_complete_body_on_trailer_endstream()
    {
        var ops = new FakeServerOps();
        var baseOptions = new GaudiServerOptions();
        var options = baseOptions.ToHttp2Options();
        var encoder = new HpackEncoder(useHuffman: false);
        var sm = new Http2ServerSessionManager(options, ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var requestHeaders = new List<HpackHeader>
        {
            new(":method", "POST"),
            new(":path", "/upload"),
            new(":scheme", "https"),
            new(":authority", "localhost"),
        };
        var headersFrame = BuildHeadersFrame(1, requestHeaders, endStream: false, encoder);
        sm.DecodeClientData(WrapFrame(headersFrame));

        var dataFrame = BuildDataFrame(1, "data"u8.ToArray(), endStream: false);
        sm.DecodeClientData(WrapFrame(dataFrame));

        var request = ops.Requests[0];
        var body = request.Get<IHttpRequestFeature>()!.Body;
        Assert.NotNull(body);

        var readBuffer = new byte[64];
        var readTask = body.ReadAsync(readBuffer, 0, readBuffer.Length, TestContext.Current.CancellationToken);

        var trailerHeaders = new List<HpackHeader>
        {
            new("x-trailer", "value"),
        };
        var trailerFrame = BuildHeadersFrame(1, trailerHeaders, endStream: true, encoder);
        sm.DecodeClientData(WrapFrame(trailerFrame));

        var read = await readTask;
        Assert.Equal(4, read);
        Assert.Equal("data"u8.ToArray(), readBuffer[..read]);
    }
}
