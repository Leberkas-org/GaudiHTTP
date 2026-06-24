using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server.SessionManager;

/// <summary>
/// Regression tests for the HeadersTimeout timer leak in Http2ServerSessionManager.CloseStream().
/// Previously, CloseStream() cancelled BodyConsumptionTimerKey but omitted HeadersTimeoutTimerKey,
/// leaving the headers-timeout:&lt;id&gt; timer armed after the stream was closed.
/// </summary>
public sealed class Http2HeadersTimerLeakSpec
{
    private static ReadOnlyMemory<byte> EncodeMinimalHeaders()
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var headers = new List<HpackHeader>
        {
            new(":method", "GET"),
            new(":path", "/"),
            new(":scheme", "https"),
            new(":authority", "localhost"),
        };
        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = encoder.Encode(headers, ref span, useHuffman: false);
        return new Memory<byte>(buffer, 0, written);
    }

    private static byte[] BuildHeadersFrame(
        int streamId,
        ReadOnlyMemory<byte> headerBlock,
        bool endStream = false,
        bool endHeaders = true)
    {
        const int h = 9;
        var frame = new byte[h + headerBlock.Length];
        var length = headerBlock.Length;
        frame[0] = (byte)(length >> 16);
        frame[1] = (byte)(length >> 8);
        frame[2] = (byte)length;
        frame[3] = (byte)FrameType.Headers;
        byte flags = 0;
        if (endStream)
        {
            flags |= 0x01;
        }

        if (endHeaders)
        {
            flags |= 0x04;
        }

        frame[4] = flags;
        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;
        headerBlock.Span.CopyTo(frame.AsSpan(h));
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
        var options = new GaudiServerOptions().ToHttp2Options();
        var sm = new Http2ServerSessionManager(options, ops);
        sm.PreStart();
        ops.Outbound.Clear();
        ops.ScheduledTimers.Clear();
        ops.CancelledTimers.Clear();
        return sm;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void EmitRstStream_should_cancel_headers_timeout_timer()
    {
        // Regression: CloseStream() previously did NOT cancel HeadersTimeoutTimerKey.
        // When the server emits RST_STREAM (e.g. to refuse or abort a stream), the
        // headers-timeout:<id> timer was left armed, leaking a timer handle.
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);

        var headerBlock = EncodeMinimalHeaders();

        // Open stream 1 fully (END_HEADERS so no CONTINUATION expected)
        var headersFrame = BuildHeadersFrame(streamId: 1, headerBlock, endStream: false, endHeaders: true);
        sm.DecodeClientData(WrapFrame(headersFrame));
        Assert.Equal(1, sm.ActiveStreamCount);

        ops.CancelledTimers.Clear();

        // Server sends RST_STREAM — calls CloseStream internally
        sm.EmitRstStream(streamId: 1, Http2ErrorCode.Cancel);

        // HeadersTimeoutTimerKey must be in the cancelled list (even if it was never scheduled,
        // a defensive cancel is the correct behaviour)
        Assert.Contains(ops.CancelledTimers, name => name.StartsWith("headers-timeout:1"));
        Assert.Equal(0, sm.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void EmitRstStream_should_cancel_both_body_and_headers_timers()
    {
        // Combined guard: both BodyConsumptionTimerKey and HeadersTimeoutTimerKey must be
        // cancelled — protects against partial-fix regressions.
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);

        var headerBlock = EncodeMinimalHeaders();

        var headersFrame = BuildHeadersFrame(streamId: 1, headerBlock, endStream: false, endHeaders: true);
        sm.DecodeClientData(WrapFrame(headersFrame));
        Assert.Equal(1, sm.ActiveStreamCount);

        ops.CancelledTimers.Clear();

        sm.EmitRstStream(streamId: 1, Http2ErrorCode.InternalError);

        Assert.Contains(ops.CancelledTimers, name => name.StartsWith("headers-timeout:1"));
        Assert.Contains(ops.CancelledTimers, name => name.StartsWith("body-consumption:1"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void CloseStream_via_refused_stream_should_cancel_headers_timeout_timer()
    {
        // When MaxConcurrentStreams=1 is reached, stream N+1 is closed via EmitRstStream.
        // Verify the headers-timeout timer for that stream is cancelled.
        var ops = new FakeServerOps();
        var baseOptions = new GaudiServerOptions { Http2 = { MaxConcurrentStreams = 1 } };
        var sm = new Http2ServerSessionManager(baseOptions.ToHttp2Options(), ops);
        sm.PreStart();
        ops.Outbound.Clear();
        ops.ScheduledTimers.Clear();
        ops.CancelledTimers.Clear();

        var headerBlock = EncodeMinimalHeaders();

        // Stream 1 occupies the only slot
        var headers1 = BuildHeadersFrame(streamId: 1, headerBlock, endStream: false, endHeaders: true);
        sm.DecodeClientData(WrapFrame(headers1));
        Assert.Equal(1, sm.ActiveStreamCount);

        ops.CancelledTimers.Clear();

        // Server-side RST_STREAM — timer must be cancelled
        sm.EmitRstStream(streamId: 1, Http2ErrorCode.Cancel);

        Assert.Contains(ops.CancelledTimers, name => name.StartsWith("headers-timeout:1"));
        Assert.Equal(0, sm.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void CloseStream_on_multiple_streams_should_cancel_respective_timer_keys()
    {
        // Each stream's CloseStream call must cancel that stream's own timer keys,
        // not a shared key — verifies per-stream key naming.
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);

        var headerBlock = EncodeMinimalHeaders();

        var headers1 = BuildHeadersFrame(streamId: 1, headerBlock, endStream: false, endHeaders: true);
        sm.DecodeClientData(WrapFrame(headers1));

        var headers3 = BuildHeadersFrame(streamId: 3, headerBlock, endStream: false, endHeaders: true);
        sm.DecodeClientData(WrapFrame(headers3));

        Assert.Equal(2, sm.ActiveStreamCount);

        ops.CancelledTimers.Clear();

        sm.EmitRstStream(streamId: 1, Http2ErrorCode.Cancel);
        Assert.Contains(ops.CancelledTimers, name => name == "headers-timeout:1");
        Assert.DoesNotContain(ops.CancelledTimers, name => name == "headers-timeout:3");

        ops.CancelledTimers.Clear();

        sm.EmitRstStream(streamId: 3, Http2ErrorCode.Cancel);
        Assert.Contains(ops.CancelledTimers, name => name == "headers-timeout:3");
        Assert.DoesNotContain(ops.CancelledTimers, name => name == "headers-timeout:1");
    }
}
