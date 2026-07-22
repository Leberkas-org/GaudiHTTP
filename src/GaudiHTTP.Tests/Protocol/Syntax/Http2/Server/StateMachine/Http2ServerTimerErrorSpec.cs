using System.Buffers;
using GaudiHTTP.Tests.TestSupport;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server.StateMachine;

public sealed class Http2ServerTimerErrorSpec
{
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
        if (endStream) flags |= 0x01; // END_STREAM
        frame[4] = flags;
        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;
        block.Span.CopyTo(frame.AsSpan(h));
        return frame;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void PreStart_should_schedule_keep_alive_timer()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        sm.PreStart();

        // Should have scheduled keep-alive timer
        var keepAliveTimer = ops.ScheduledTimers.FirstOrDefault(t => t.Name == "keep-alive-timeout");
        Assert.NotEqual(default, keepAliveTimer);
        Assert.True(keepAliveTimer.Delay > TimeSpan.Zero, "Keep-alive timeout should be positive");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void OnTimerFired_keep_alive_should_emit_GoAway()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        sm.PreStart();
        var transport = sm.ConnectTransport(ops: ops);
        transport.TakeWrittenBytes();

        sm.OnTimerFired("keep-alive-timeout");

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(transport.WrittenMemory), out _).ToList();
        var goAway = frames.OfType<GoAwayFrame>().FirstOrDefault();
        Assert.NotNull(goAway);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void ShouldComplete_should_always_be_false()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        Assert.False(sm.ShouldComplete);

        sm.PreStart();
        Assert.False(sm.ShouldComplete);

        // Decode a HEADERS frame to open a stream
        var headersFrame = BuildHeadersFrame(streamId: 1, endStream: true);
        var transport = sm.ConnectTransport(initialData: headersFrame, ops: ops);

        // ShouldComplete should still be false for HTTP/2
        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeClientData_should_cancel_keep_alive_when_streams_open()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        sm.PreStart();
        ops.CancelledTimers.Clear();

        // Decode a HEADERS frame to open a stream
        var headersFrame = BuildHeadersFrame(streamId: 1, endStream: false);
        var transport = sm.ConnectTransport(initialData: headersFrame, ops: ops);

        // Keep-alive timer should be cancelled when streams open
        Assert.Contains("keep-alive-timeout", ops.CancelledTimers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void OnTimerFired_headers_timeout_should_emit_RstStream()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        sm.PreStart();
        var transport = sm.ConnectTransport(ops: ops);
        transport.TakeWrittenBytes();

        sm.OnTimerFired("headers-timeout:1");

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(transport.WrittenMemory), out _).ToList();
        var rstStream = frames.OfType<RstStreamFrame>().FirstOrDefault();
        Assert.NotNull(rstStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Cleanup_should_be_idempotent()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        sm.PreStart();

        // Should not throw when called multiple times
        sm.Cleanup();
        sm.Cleanup();
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void OnResponse_for_unknown_stream_should_not_crash()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        sm.PreStart();
        var transport = sm.ConnectTransport(ops: ops);

        // Should not throw when responding on unknown stream
        var context = ServerTestContext.CreateStreamResponse(999);
        sm.OnResponse(context);

        Assert.True(true);
    }
}