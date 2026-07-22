using System.Buffers;
using GaudiHTTP.Tests.TestSupport;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server.Streaming;

public sealed class Http2ServerInboundBodyBackpressureSpec
{
    private static byte[] BuildHeadersFrame(int streamId, ReadOnlyMemory<byte> headerBlock, bool endStream = false,
        bool endHeaders = true)
    {
        const int frameHeaderSize = 9;
        var frameSize = frameHeaderSize + headerBlock.Length;
        var frame = new byte[frameSize];

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

    private static byte[] BuildDataFrame(int streamId, byte[] data, bool endStream = false)
    {
        const int frameHeaderSize = 9;
        var frameSize = frameHeaderSize + data.Length;
        var frame = new byte[frameSize];

        var length = data.Length;
        frame[0] = (byte)(length >> 16);
        frame[1] = (byte)(length >> 8);
        frame[2] = (byte)length;
        frame[3] = (byte)FrameType.Data;

        byte flags = 0;
        if (endStream) flags |= (byte)Datas.EndStream;
        frame[4] = flags;

        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;

        data.CopyTo(frame.AsSpan(frameHeaderSize));

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

    private static (Http2ServerStateMachine Sm, FakeServerOps Ops, InMemoryTransport Transport) CreateSm(
        int streamWindowSize = 16384,
        int connectionWindowSize = 65535)
    {
        var ops = new FakeServerOps();
        var options = new GaudiServerOptions
        {
            Http2 =
            {
                MaxConcurrentStreams = 100,
                InitialConnectionWindowSize = connectionWindowSize,
                InitialStreamWindowSize = streamWindowSize
            }
        };
        var sm = new Http2ServerStateMachine(options.ToHttp2Options(), ops);
        sm.PreStart();
        var transport = sm.ConnectTransport(ops: ops);
        ops.Outbound.Clear();
        return (sm, ops, transport);
    }

    private static void SendHeaders(Http2ServerStateMachine sm, FakeServerOps ops, InMemoryTransport transport, int streamId)
    {
        var headerBlock = EncodeHeaders("POST", "/upload", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId, headerBlock, endStream: false, endHeaders: true);
        transport.FeedMore(sm, ops, headersFrameData);
    }

    private static void SendData(Http2ServerStateMachine sm, FakeServerOps ops, InMemoryTransport transport, int streamId, int bytes, bool endStream = false)
    {
        const int maxFramePayload = 16 * 1024;
        var remaining = bytes;
        while (remaining > 0)
        {
            var chunk = Math.Min(remaining, maxFramePayload);
            remaining -= chunk;
            var isLast = remaining == 0;
            var payload = new byte[chunk];
            var frame = BuildDataFrame(streamId, payload, endStream && isLast);
            transport.FeedMore(sm, ops, frame);
        }
    }

    private static bool HasWindowUpdateForStream(InMemoryTransport transport, int streamId)
    {
        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(transport.WrittenMemory), out _);
        return frames.OfType<WindowUpdateFrame>().Any(wf => wf.StreamId == streamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "Protocol")]
    public void HandleDataFrame_should_not_emit_stream_window_update_before_app_reads()
    {
        // Arrange: small stream window so threshold is crossed with ~9 KB
        var (sm, ops, transport) = CreateSm(streamWindowSize: 16384);
        SendHeaders(sm, ops, transport, streamId: 1);
        transport.TakeWrittenBytes();

        // Act: send 9000 bytes — crosses the 8192-byte (half-window) threshold
        SendData(sm, ops, transport, streamId: 1, bytes: 9000);

        // Assert: stream-level WU for stream 1 must NOT appear before app reads
        Assert.False(
            HasWindowUpdateForStream(transport, streamId: 1),
            "Stream-level WINDOW_UPDATE must not be emitted before the application reads body data");
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "Protocol")]
    public void HandleDataFrame_should_emit_deferred_stream_window_update_after_app_reads()
    {
        // Arrange
        var (sm, ops, transport) = CreateSm(streamWindowSize: 16384);
        SendHeaders(sm, ops, transport, streamId: 1);
        ops.Outbound.Clear();

        // Send enough data to accumulate a deferred stream increment
        SendData(sm, ops, transport, streamId: 1, bytes: 9000);
        Assert.False(HasWindowUpdateForStream(transport, streamId: 1));
        transport.TakeWrittenBytes();

        // Act: simulate app consuming a body slot — fires SlotFreed -> OnBodyMessage(StreamBodyConsumed)
        sm.OnBodyMessage(new Http2ServerSessionManager.StreamBodyConsumed(1));

        // Assert: deferred stream WU now emitted for stream 1
        Assert.True(
            HasWindowUpdateForStream(transport, streamId: 1),
            "Stream-level WINDOW_UPDATE must be emitted after app consumes body data");
    }

    [Fact(Timeout = 5000)]
    [Trait("Category", "Protocol")]
    public void HandleDataFrame_should_always_emit_connection_window_update_immediately()
    {
        // Arrange: large stream window, small connection window so connection threshold is crossed
        var (sm, ops, transport) = CreateSm(streamWindowSize: 65535, connectionWindowSize: 65535);
        SendHeaders(sm, ops, transport, streamId: 1);
        transport.TakeWrittenBytes();

        // Act: 33000 bytes crosses the ~32767 (half of 65535) connection-level threshold
        SendData(sm, ops, transport, streamId: 1, bytes: 33000);

        // Assert: connection-level WU (stream 0) must appear immediately — no deferral
        Assert.True(
            HasWindowUpdateForStream(transport, streamId: 0),
            "Connection-level WINDOW_UPDATE must always be emitted immediately regardless of app reads");
    }
}
