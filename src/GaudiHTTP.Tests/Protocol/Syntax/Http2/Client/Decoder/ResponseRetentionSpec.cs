using System.Buffers;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Client;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;
using Servus.Akka.Transport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client.Decoder;

public sealed class ResponseRetentionSpec
{
    private static HttpRequestMessage MakeGet(string path = "/")
        => new(HttpMethod.Get, $"https://example.com{path}");

    private static HeadersFrame MakeResponseHeaders(int streamId, bool endStream = true)
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var hpack = encoder.Encode([(":status", "200"), ("content-type", "text/plain")]);
        return new HeadersFrame(streamId, hpack, endStream, endHeaders: true);
    }

    private static byte[] SerializeFrameBytes(Http2Frame frame)
    {
        var bytes = new byte[frame.SerializedSize];
        var span = bytes.AsSpan();
        frame.WriteTo(ref span);
        return bytes;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void StateMachine_should_retain_response_when_rst_stream_no_error_follows_headers()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(), ops);
        sm.PreStart();

        // Send a request
        sm.OnRequest(MakeGet());

        // Simulate server sending response headers without END_STREAM, then RST_STREAM with NO_ERROR
        // The response should be retained and emitted to the caller
        var headersFrame = MakeResponseHeaders(1, endStream: false);
        var headersBytes = SerializeFrameBytes(headersFrame);

        var transport = sm.ConnectTransport(initialData: headersBytes, ops: ops);

        // After headers without END_STREAM, response should be available
        Assert.Single(ops.Responses);

        // Now send RST_STREAM with NO_ERROR
        var rstFrame = new RstStreamFrame(1, Http2ErrorCode.NoError);
        var rstBytes = SerializeFrameBytes(rstFrame);

        transport.FeedMore(sm, ops, rstBytes);

        // Response should still be retained (still single response)
        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.7")]
    public void FrameDecoder_should_decode_refused_stream_error_code()
    {
        var decoder = new FrameDecoder();
        var rstFrame = new RstStreamFrame(1, Http2ErrorCode.RefusedStream);
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(rstFrame.Serialize()), out _);

        var rst = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(Http2ErrorCode.RefusedStream, rst.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.7")]
    public void FrameDecoder_should_decode_no_error_code()
    {
        var decoder = new FrameDecoder();
        var rstFrame = new RstStreamFrame(1, Http2ErrorCode.NoError);
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(rstFrame.Serialize()), out _);

        var rst = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(Http2ErrorCode.NoError, rst.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void FrameDecoder_should_preserve_stream_id_in_rst_stream()
    {
        var decoder = new FrameDecoder();
        var rstFrame = new RstStreamFrame(42, Http2ErrorCode.Cancel);
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(rstFrame.Serialize()), out _);

        var rst = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(42, rst.StreamId);
    }
}
