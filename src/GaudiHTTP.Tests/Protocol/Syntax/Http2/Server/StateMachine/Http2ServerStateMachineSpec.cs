using System.Buffers;
using GaudiHTTP.Tests.TestSupport;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server.StateMachine;

public sealed class Http2ServerStateMachineSpec
{
    private static byte[] BuildHeadersFrame(int streamId, ReadOnlyMemory<byte> headerBlock, bool endStream = false,
        bool endHeaders = true)
    {
        const int frameHeaderSize = 9;
        var frameSize = frameHeaderSize + headerBlock.Length;
        var frame = new byte[frameSize];

        // Frame header: length (3 bytes), type (1), flags (1), stream ID (4)
        var length = headerBlock.Length;
        frame[0] = (byte)(length >> 16);
        frame[1] = (byte)(length >> 8);
        frame[2] = (byte)length;
        frame[3] = (byte)FrameType.Headers;

        byte flags = 0;
        if (endStream) flags |= (byte)Headers.EndStream;
        if (endHeaders) flags |= (byte)Headers.EndHeaders;
        frame[4] = flags;

        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;

        headerBlock.Span.CopyTo(frame.AsSpan(frameHeaderSize));

        return frame;
    }

    private static byte[] BuildSettingsFrame(bool isAck = false)
    {
        const int frameHeaderSize = 9;
        var frame = new byte[frameHeaderSize];

        frame[0] = 0;
        frame[1] = 0;
        frame[2] = 0;
        frame[3] = (byte)FrameType.Settings;
        frame[4] = isAck ? (byte)Settings.Ack : (byte)0;
        frame[5] = 0;
        frame[6] = 0;
        frame[7] = 0;
        frame[8] = 0;

        return frame;
    }

    private static byte[] BuildPingFrame(bool isAck = false)
    {
        const int frameHeaderSize = 9;
        const int pingDataSize = 8;
        const int frameSize = frameHeaderSize + pingDataSize;
        var frame = new byte[frameSize];

        frame[0] = 0;
        frame[1] = 0;
        frame[2] = pingDataSize;
        frame[3] = (byte)FrameType.Ping;
        frame[4] = isAck ? (byte)Pings.Ack : (byte)0;
        frame[5] = 0;
        frame[6] = 0;
        frame[7] = 0;
        frame[8] = 0;

        // Ping data (8 bytes of arbitrary data)
        for (var i = 0; i < pingDataSize; i++)
        {
            frame[frameHeaderSize + i] = (byte)i;
        }

        return frame;
    }

    private static ReadOnlyMemory<byte> EncodeHeaders(string method, string path, string authority = "localhost")
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var headers = new List<HpackHeader>
        {
            new(":method", method),
            new(":path", path),
            new(":scheme", "https"),
            new(":authority", authority),
        };

        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = encoder.Encode(headers, ref span, useHuffman: true);

        return new Memory<byte>(buffer, 0, written);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.2")]
    public void PreStart_should_emit_settings_frame()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        sm.PreStart();
        var transport = sm.ConnectTransport(ops: ops);

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(transport.WrittenMemory), out _).ToList();
        Assert.True(frames.Count >= 2, $"Expected at least 2 frames on PreStart, got {frames.Count}");
        Assert.Contains(frames, f => f is SettingsFrame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeClientData_with_headers_should_produce_request_with_stream_id()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        var headerBlock = EncodeHeaders("GET", "/", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: true, endHeaders: true);

        var transport = sm.ConnectTransport(initialData: headersFrameData, ops: ops);

        Assert.Single(ops.Requests);
        var context = ops.Requests[0];

        // Verify stream ID was stored in request features
        var streamIdFeature = context.Get<IHttpStreamIdFeature>();
        Assert.NotNull(streamIdFeature);
        Assert.Equal(1, streamIdFeature.StreamId);

        // Verify request properties
        Assert.Equal("GET", context.Get<IHttpRequestFeature>()?.Method);
        Assert.Equal("/", context.Get<IHttpRequestFeature>()?.Path);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeClientData_with_headers_incomplete_should_not_emit_request_until_end_headers()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        var headerBlock = EncodeHeaders("GET", "/", "example.com");
        // Split header block: first part without EndHeaders
        var partSize = headerBlock.Length / 2;
        var headersFrameData = BuildHeadersFrame(
            streamId: 1,
            headerBlock[..partSize],
            endStream: false,
            endHeaders: false);

        var transport = sm.ConnectTransport(initialData: headersFrameData, ops: ops);

        // No request emitted yet, waiting for CONTINUATION
        Assert.Empty(ops.Requests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void DecodeClientData_with_ping_should_echo_ack()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        sm.PreStart();

        var transport = sm.ConnectTransport(ops: ops);
        transport.TakeWrittenBytes();

        var pingFrameData = BuildPingFrame(isAck: false);
        transport.FeedMore(sm, ops, pingFrameData);

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(transport.WrittenMemory), out _).ToList();
        var pingAck = frames.OfType<PingFrame>().FirstOrDefault(p => p.IsAck);
        Assert.NotNull(pingAck);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void DecodeClientData_with_settings_should_ack()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        sm.PreStart();

        var transport = sm.ConnectTransport(ops: ops);
        transport.TakeWrittenBytes();

        var settingsFrameData = BuildSettingsFrame(isAck: false);
        transport.FeedMore(sm, ops, settingsFrameData);

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(transport.WrittenMemory), out _).ToList();
        var settingsAck = frames.OfType<SettingsFrame>().FirstOrDefault(s => s.IsAck);
        Assert.NotNull(settingsAck);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void OnResponse_should_encode_and_emit_frames()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        // Receive a request first
        var headerBlock = EncodeHeaders("GET", "/", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: true, endHeaders: true);

        var transport = sm.ConnectTransport(initialData: headersFrameData, ops: ops);

        Assert.Single(ops.Requests);

        // Now send a response
        transport.TakeWrittenBytes();
        var requestContext = ops.Requests[0];
        requestContext.Get<IHttpResponseFeature>()!.StatusCode = 200;
        sm.OnResponse(requestContext);

        // Should emit response frames via transport
        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(transport.WrittenMemory), out _).ToList();
        Assert.NotEmpty(frames);

        // At minimum, should have HEADERS frame
        Assert.Contains(frames, f => f is HeadersFrame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void CanAcceptResponse_should_be_true_when_request_received()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        Assert.False(sm.CanAcceptResponse);

        var headerBlock = EncodeHeaders("GET", "/", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId: 1, headerBlock, endStream: true, endHeaders: true);

        var transport = sm.ConnectTransport(initialData: headersFrameData, ops: ops);

        Assert.True(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void Cleanup_should_dispose_decoder()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        sm.PreStart();

        // Should not throw
        sm.Cleanup();
    }
}