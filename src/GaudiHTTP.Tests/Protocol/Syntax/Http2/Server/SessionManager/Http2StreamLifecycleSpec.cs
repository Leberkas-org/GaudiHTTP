using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server.SessionManager;

public sealed class Http2StreamLifecycleSpec
{
    private static IFeatureCollection CreateResponseContext(long streamId = 99)
    {
        var features = new GaudiFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new GaudiHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new GaudiHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpStreamIdFeature>(new GaudiStreamIdFeature(streamId));
        return features;
    }

    private static byte[] BuildHeadersFrame(int streamId, bool endStream = false)
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

    private static byte[] BuildRstStreamFrame(int streamId, uint errorCode)
    {
        const int h = 9;
        var frame = new byte[h + 4];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = 4;
        frame[3] = (byte)FrameType.RstStream;
        frame[4] = 0;
        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;
        frame[9] = (byte)(errorCode >> 24);
        frame[10] = (byte)(errorCode >> 16);
        frame[11] = (byte)(errorCode >> 8);
        frame[12] = (byte)errorCode;
        return frame;
    }

    private static TransportBuffer WrapFrame(byte[] frame)
    {
        var buffer = TransportBuffer.Rent(frame.Length);
        frame.CopyTo(buffer.FullMemory.Span);
        buffer.Length = frame.Length;
        return buffer;
    }

    private static byte[] BuildWindowUpdateFrame(int streamId, int increment)
    {
        const int h = 9;
        var frame = new byte[h + 4];
        frame[2] = 4;
        frame[3] = (byte)FrameType.WindowUpdate;
        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;
        var inc = increment & 0x7FFFFFFF;
        frame[9] = (byte)(inc >> 24);
        frame[10] = (byte)(inc >> 16);
        frame[11] = (byte)(inc >> 8);
        frame[12] = (byte)inc;
        return frame;
    }

    // Walks the emitted H2 frames and sums DATA payload bytes for one stream, plus whether an
    // END_STREAM-flagged DATA frame was seen.
    private static (long DataBytes, bool EndStream) SumData(IEnumerable<object> outbound, int streamId)
    {
        long total = 0;
        var endStream = false;
        foreach (var td in outbound.OfType<TransportData>())
        {
            var span = td.Buffer.Span;
            var pos = 0;
            while (pos + 9 <= span.Length)
            {
                var len = (span[pos] << 16) | (span[pos + 1] << 8) | span[pos + 2];
                var type = span[pos + 3];
                var flags = span[pos + 4];
                var sid = (span[pos + 5] << 24) | (span[pos + 6] << 16) | (span[pos + 7] << 8) | span[pos + 8];
                if (type == (byte)FrameType.Data && sid == streamId)
                {
                    total += len;
                    if ((flags & 0x01) != 0)
                    {
                        endStream = true;
                    }
                }

                pos += 9 + len;
            }
        }

        return (total, endStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Buffered_response_exceeding_send_window_holds_slice_and_drains_on_window_update()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerSessionManager(new GaudiServerOptions().ToHttp2Options(), ops);
        sm.PreStart();
        sm.DecodeClientData(WrapFrame(BuildHeadersFrame(streamId: 1, endStream: true)));
        ops.Outbound.Clear();

        // Buffered response body larger than the default 65535 send window.
        const int bodySize = 100_000;
        var context = CreateResponseContext(streamId: 1);
        var bodyFeature = (GaudiHttpResponseBodyFeature)context.Get<IHttpResponseBodyFeature>()!;
        bodyFeature.Writer.GetMemory(bodySize).Span[..bodySize].Fill(0xAB);
        bodyFeature.Writer.Advance(bodySize);
        bodyFeature.Writer.Complete(); // completed + not upgraded => buffered (TryGetBufferedBody)

        sm.OnResponse(context);

        // Window-blocked: only the window's worth is emitted, no END_STREAM, stream still open and
        // holding the unsent slice (no ToArray copy, no pump).
        var afterResponse = SumData(ops.Outbound, streamId: 1);
        Assert.True(afterResponse.DataBytes is > 0 and <= 65535,
            $"expected partial emit within the window, got {afterResponse.DataBytes}");
        Assert.False(afterResponse.EndStream);
        Assert.Equal(1, sm.ActiveStreamCount);

        // Replenish connection + stream windows; the held slice drains directly.
        sm.DecodeClientData(WrapFrame(BuildWindowUpdateFrame(streamId: 0, increment: bodySize)));
        sm.DecodeClientData(WrapFrame(BuildWindowUpdateFrame(streamId: 1, increment: bodySize)));

        var afterUpdate = SumData(ops.Outbound, streamId: 1);
        Assert.Equal(bodySize, afterUpdate.DataBytes);
        Assert.True(afterUpdate.EndStream);
        Assert.Equal(0, sm.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public void Should_accept_streams_up_to_max_concurrent()
    {
        var ops = new FakeServerOps();
        var baseOptions = new GaudiServerOptions { Http2 = { MaxConcurrentStreams = 2 } };
        var options = baseOptions.ToHttp2Options();

        var sm = new Http2ServerSessionManager(options, ops);

        sm.PreStart();
        ops.Outbound.Clear(); // Clear initial SETTINGS frame
        ops.ScheduledTimers.Clear();

        // Step 1: Send HEADERS on stream 1 with endStream=true
        var headers1 = BuildHeadersFrame(streamId: 1, endStream: true);
        sm.DecodeClientData(WrapFrame(headers1));

        // Stream 1 should be accepted
        Assert.Single(ops.Requests);
        Assert.Equal(1, sm.ActiveStreamCount);

        // Step 2: Send HEADERS on stream 3 with endStream=true
        var headers3 = BuildHeadersFrame(streamId: 3, endStream: true);
        sm.DecodeClientData(WrapFrame(headers3));

        // Stream 3 should be accepted (we're at max=2 concurrent)
        Assert.Equal(2, ops.Requests.Count);
        Assert.Equal(2, sm.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public void Should_refuse_stream_above_max_concurrent()
    {
        var ops = new FakeServerOps();
        var baseOptions = new GaudiServerOptions { Http2 = { MaxConcurrentStreams = 1 } };
        var options = baseOptions.ToHttp2Options();

        var sm = new Http2ServerSessionManager(options, ops);

        sm.PreStart();
        ops.Outbound.Clear(); // Clear initial SETTINGS frame
        ops.ScheduledTimers.Clear();

        // Step 1: Send HEADERS on stream 1 with endStream=false (stream stays open)
        var headers1 = BuildHeadersFrame(streamId: 1, endStream: false);
        sm.DecodeClientData(WrapFrame(headers1));

        // Stream 1 should be accepted and stay open
        Assert.Single(ops.Requests);
        Assert.Equal(1, sm.ActiveStreamCount);

        // Clear outbound to detect the RST_STREAM for stream 3
        ops.Outbound.Clear();

        // Step 2: Send HEADERS on stream 3 (should be refused)
        var headers3 = BuildHeadersFrame(streamId: 3, endStream: false);
        sm.DecodeClientData(WrapFrame(headers3));

        // No new request should be emitted for stream 3
        Assert.Single(ops.Requests);

        // RST_STREAM should be emitted
        Assert.NotEmpty(ops.Outbound);
        var foundRstStream = false;
        foreach (var outbound in ops.Outbound)
        {
            if (outbound is TransportData { Buffer.Length: >= 9 } td)
            {
                var span = td.Buffer.FullMemory.Span;
                var frameType = (FrameType)span[3];
                if (frameType == FrameType.RstStream)
                {
                    foundRstStream = true;
                    break;
                }
            }
        }

        Assert.True(foundRstStream, "RST_STREAM frame not found");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void RstStream_on_active_stream_should_close_it()
    {
        var ops = new FakeServerOps();
        var baseOptions = new GaudiServerOptions();
        var options = baseOptions.ToHttp2Options();

        var sm = new Http2ServerSessionManager(options, ops);

        sm.PreStart();
        ops.Outbound.Clear();
        ops.ScheduledTimers.Clear();

        // Step 1: Send HEADERS on stream 1
        var headersFrame = BuildHeadersFrame(streamId: 1, endStream: false);
        sm.DecodeClientData(WrapFrame(headersFrame));

        // Stream 1 should be active
        Assert.Single(ops.Requests);
        Assert.Equal(1, sm.ActiveStreamCount);

        // Step 2: Send RST_STREAM on stream 1
        var rstFrame = BuildRstStreamFrame(streamId: 1, errorCode: 0u);
        sm.DecodeClientData(WrapFrame(rstFrame));

        // Stream should be closed
        Assert.Equal(0, sm.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void RstStream_on_closed_stream_should_not_crash()
    {
        var ops = new FakeServerOps();
        var baseOptions = new GaudiServerOptions();
        var options = baseOptions.ToHttp2Options();
        var sm = new Http2ServerSessionManager(options, ops);

        sm.PreStart();
        ops.Outbound.Clear();
        ops.ScheduledTimers.Clear();

        // Send RST_STREAM on stream 99 (never opened)
        var rstFrame = BuildRstStreamFrame(streamId: 99, errorCode: 0u);

        // Should not throw
        sm.DecodeClientData(WrapFrame(rstFrame));

        // No request should be emitted
        Assert.Empty(ops.Requests);
        Assert.Equal(0, sm.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void Headers_with_EndStream_true_should_emit_request_immediately()
    {
        var ops = new FakeServerOps();
        var baseOptions = new GaudiServerOptions();
        var options = baseOptions.ToHttp2Options();
        var sm = new Http2ServerSessionManager(options, ops);

        sm.PreStart();
        ops.Outbound.Clear();
        ops.ScheduledTimers.Clear();

        // Send HEADERS with endStream=true
        var headersFrame = BuildHeadersFrame(streamId: 1, endStream: true);
        sm.DecodeClientData(WrapFrame(headersFrame));

        // Exactly one request should be emitted
        Assert.Single(ops.Requests);
        var context = ops.Requests[0];

        // Request should have stream ID set
        var streamIdFeature = context.Get<IHttpStreamIdFeature>();
        Assert.NotNull(streamIdFeature);
        Assert.Equal(1, streamIdFeature.StreamId);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_be_idempotent()
    {
        var ops = new FakeServerOps();
        var baseOptions = new GaudiServerOptions();
        var options = baseOptions.ToHttp2Options();
        var sm = new Http2ServerSessionManager(options, ops);

        sm.PreStart();
        ops.Outbound.Clear();
        ops.ScheduledTimers.Clear();

        // Send HEADERS on stream 1
        var headersFrame = BuildHeadersFrame(streamId: 1, endStream: false);
        sm.DecodeClientData(WrapFrame(headersFrame));

        // Stream should be active
        Assert.Equal(1, sm.ActiveStreamCount);

        // First cleanup
        sm.Cleanup();
        Assert.Equal(0, sm.ActiveStreamCount);

        // Second cleanup (should not crash)
        sm.Cleanup();
        Assert.Equal(0, sm.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_for_unknown_stream_should_not_crash()
    {
        var ops = new FakeServerOps();
        var baseOptions = new GaudiServerOptions();
        var options = baseOptions.ToHttp2Options();
        var sm = new Http2ServerSessionManager(options, ops);

        sm.PreStart();
        ops.Outbound.Clear();
        ops.ScheduledTimers.Clear();

        // Should not throw when responding on unknown stream
        var context = CreateResponseContext();
        sm.OnResponse(context);

        // No crash, test passes
    }
}