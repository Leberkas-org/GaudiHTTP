using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.SessionManager;

public sealed class Http2Server1xxSpec
{
    private static Http2ConnectionOptions DefaultOptions() => new TurboServerOptions().ToHttp2Options();

    private static byte[] BuildHeadersFrame(int streamId, bool endStream = true)
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
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
        var len = block.Length;
        frame[0] = (byte)(len >> 16);
        frame[1] = (byte)(len >> 8);
        frame[2] = (byte)len;
        frame[3] = (byte)FrameType.Headers;
        byte flags = 0x04; // END_HEADERS
        if (endStream) flags |= 0x01;
        frame[4] = flags;
        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;
        block.Span.CopyTo(frame.AsSpan(h));
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
    public void Feature_should_be_registered_on_request_dispatch()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerSessionManager(DefaultOptions(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        sm.DecodeClientData(WrapFrame(BuildHeadersFrame(1)));

        Assert.Single(ops.Requests);
        var feature = ops.Requests[0].Get<TurboInformationalResponseFeature>();
        Assert.NotNull(feature);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void SendInformational_should_not_close_stream()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerSessionManager(DefaultOptions(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        sm.DecodeClientData(WrapFrame(BuildHeadersFrame(1)));
        var features = ops.Requests[0];

        features.Get<TurboInformationalResponseFeature>()!
            .SendInformational(100, new HeaderDictionary());

        // Stream should still be open — final response should succeed
        var responseFeature = features.Get<IHttpResponseFeature>()!;
        responseFeature.StatusCode = 200;
        var ex = Record.Exception(() => sm.OnResponse(features));

        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void SendInformational_should_emit_outbound_data()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerSessionManager(DefaultOptions(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        sm.DecodeClientData(WrapFrame(BuildHeadersFrame(1)));
        var features = ops.Requests[0];
        ops.Outbound.Clear();

        features.Get<TurboInformationalResponseFeature>()!
            .SendInformational(103, new HeaderDictionary { ["Link"] = "</style.css>; rel=preload" });

        Assert.NotEmpty(ops.Outbound);
    }
}
