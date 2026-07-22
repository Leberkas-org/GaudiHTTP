using System.Buffers;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server.SessionManager;

/// <summary>
/// RFC 9113 §8.1: a response to a HEAD request keeps its header fields (incl. content-length) but
/// MUST NOT carry a message body — no DATA frames; the HEADERS frame ends the stream.
/// </summary>
public sealed class Http2ServerHeadResponseSpec
{
    private static ReadOnlyMemory<byte> EncodeHeaders(string method, string path)
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var headers = new List<HpackHeader>
        {
            new(":method", method),
            new(":path", path),
            new(":scheme", "https"),
            new(":authority", "example.com"),
        };

        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = encoder.Encode(headers, ref span, useHuffman: true);
        return new Memory<byte>(buffer, 0, written);
    }

    private static byte[] BuildHeadersFrame(int streamId, ReadOnlyMemory<byte> headerBlock)
    {
        var frame = new byte[9 + headerBlock.Length];
        var length = headerBlock.Length;
        frame[0] = (byte)(length >> 16);
        frame[1] = (byte)(length >> 8);
        frame[2] = (byte)length;
        frame[3] = (byte)FrameType.Headers;
        frame[4] = (byte)(Headers.EndStream | Headers.EndHeaders);
        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;
        headerBlock.Span.CopyTo(frame.AsSpan(9));
        return frame;
    }

    private static List<Http2Frame> DecodeFrames(ReadOnlyMemory<byte> data)
    {
        var decoder = new FrameDecoder();
        return decoder.DecodeAll(new ReadOnlySequence<byte>(data), out _).ToList();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void OnResponse_should_not_emit_DATA_for_HEAD_request()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);
        sm.PreStart();

        var transport = sm.ConnectTransport(initialData: BuildHeadersFrame(1, EncodeHeaders("HEAD", "/resource")), ops: ops);

        var features = ops.Requests[^1];
        features.Get<IHttpResponseFeature>()!.StatusCode = 200;
        features.Get<IHttpResponseFeature>()!.Headers["Content-Length"] = "5";
        var writer = features.Get<IHttpResponseBodyFeature>()!.Writer;
        "hello"u8.CopyTo(writer.GetMemory(5).Span);
        writer.Advance(5);
        writer.Complete();

        transport.TakeWrittenBytes(); // clear preface + initial frames
        sm.OnResponse(features);

        var frames = DecodeFrames(transport.WrittenMemory);
        Assert.Empty(frames.OfType<DataFrame>());
        Assert.Contains(frames.OfType<HeadersFrame>(), h => h.EndStream);
    }
}
