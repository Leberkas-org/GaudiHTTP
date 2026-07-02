using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server.SessionManager;

/// <summary>
/// Verifies the H2 server's buffered-vs-streaming request body decision (body buffer options
/// feature): requests with a known Content-Length at or below the configured threshold are
/// collected via <c>BufferedBodyReader</c> and dispatched to the handler only once complete,
/// instead of always streaming via <c>QueuedBodyReader</c> and dispatching at HEADERS time.
/// </summary>
public sealed class Http2ServerBufferedRequestSpec
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

    private static List<HpackHeader> PostHeaders(long contentLength) =>
    [
        new(":method", "POST"),
        new(":path", "/upload"),
        new(":scheme", "https"),
        new(":authority", "localhost"),
        new("content-length", contentLength.ToString())
    ];

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Small_request_body_should_defer_dispatch_until_body_complete()
    {
        var ops = new FakeServerOps();
        var options = new GaudiServerOptions().ToHttp2Options();
        var encoder = new HpackEncoder(useHuffman: false);
        var sm = new Http2ServerSessionManager(options, ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var body = "hello"u8.ToArray();
        var headersFrame = BuildHeadersFrame(1, PostHeaders(body.Length), endStream: false, encoder);
        sm.DecodeClientData(WrapFrame(headersFrame));

        // Headers arrived but the body has not — dispatch must be deferred (buffered path).
        Assert.Empty(ops.Requests);

        var dataFrame = BuildDataFrame(1, body, endStream: true);
        sm.DecodeClientData(WrapFrame(dataFrame));

        var request = Assert.Single(ops.Requests);
        var bodyStream = request.Get<IHttpRequestFeature>()!.Body;
        Assert.NotNull(bodyStream);

        var readBuffer = new byte[64];
        var read = await bodyStream.ReadAsync(readBuffer, TestContext.Current.CancellationToken);
        Assert.Equal(body, readBuffer[..read]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Request_body_above_threshold_should_dispatch_immediately_as_streaming()
    {
        var ops = new FakeServerOps();
        var baseOptions = new GaudiServerOptions { MaxBufferedRequestBodySize = 4 };
        var options = baseOptions.ToHttp2Options();
        var encoder = new HpackEncoder(useHuffman: false);
        var sm = new Http2ServerSessionManager(options, ops);
        sm.PreStart();
        ops.Outbound.Clear();

        // Content-Length exceeds the (deliberately tiny) threshold: must stream/dispatch immediately.
        var headersFrame = BuildHeadersFrame(1, PostHeaders(100), endStream: false, encoder);
        sm.DecodeClientData(WrapFrame(headersFrame));

        Assert.Single(ops.Requests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1.1")]
    public void Buffered_request_should_reset_stream_on_short_body()
    {
        var ops = new FakeServerOps();
        var options = new GaudiServerOptions().ToHttp2Options();
        var encoder = new HpackEncoder(useHuffman: false);
        var sm = new Http2ServerSessionManager(options, ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var headersFrame = BuildHeadersFrame(1, PostHeaders(10), endStream: false, encoder);
        sm.DecodeClientData(WrapFrame(headersFrame));
        Assert.Empty(ops.Requests);

        sm.DecodeClientData(WrapFrame(BuildDataFrame(1, "short"u8.ToArray(), endStream: true)));

        Assert.Empty(ops.Requests);
        Assert.True(ops.Outbound.Any(o =>
            o is TransportData { Buffer.Length: >= 9 } td
            && (FrameType)td.Buffer.FullMemory.Span[3] == FrameType.RstStream));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Small_request_body_split_across_multiple_DATA_frames_should_reassemble_correctly()
    {
        var ops = new FakeServerOps();
        var options = new GaudiServerOptions().ToHttp2Options();
        var encoder = new HpackEncoder(useHuffman: false);
        var sm = new Http2ServerSessionManager(options, ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var body = "hello world"u8.ToArray();
        var headersFrame = BuildHeadersFrame(1, PostHeaders(body.Length), endStream: false, encoder);
        sm.DecodeClientData(WrapFrame(headersFrame));
        Assert.Empty(ops.Requests);

        sm.DecodeClientData(WrapFrame(BuildDataFrame(1, body[..5], endStream: false)));
        Assert.Empty(ops.Requests);

        sm.DecodeClientData(WrapFrame(BuildDataFrame(1, body[5..], endStream: true)));

        var request = Assert.Single(ops.Requests);
        var bodyStream = request.Get<IHttpRequestFeature>()!.Body;
        var readBuffer = new byte[64];
        var totalRead = 0;
        int read;
        while (totalRead < body.Length &&
               (read = await bodyStream.ReadAsync(readBuffer.AsMemory(totalRead), TestContext.Current.CancellationToken)) > 0)
        {
            totalRead += read;
        }

        Assert.Equal(body, readBuffer[..totalRead]);
    }
}
