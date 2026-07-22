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

public sealed class Http2ServerStreamCorrelationSpec
{
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5")]
    public void Multiple_concurrent_streams_should_correlate_responses_to_correct_stream_ids()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        // Send HEADERS on stream 1
        var headerBlock1 = EncodeHeaders("GET", "/path1", "example.com");
        var headersFrameData1 = BuildHeadersFrame(streamId: 1, headerBlock1, endStream: true, endHeaders: true);

        var transport = sm.ConnectTransport(initialData: headersFrameData1, ops: ops);

        // Send HEADERS on stream 3
        var headerBlock3 = EncodeHeaders("GET", "/path3", "example.com");
        var headersFrameData3 = BuildHeadersFrame(streamId: 3, headerBlock3, endStream: true, endHeaders: true);

        transport.FeedMore(sm, ops, headersFrameData3);

        // Verify both requests were emitted
        Assert.Equal(2, ops.Requests.Count);

        // Verify stream IDs are correctly stored in request features
        var context1 = ops.Requests[0];
        var streamIdFeature1 = context1.Get<IHttpStreamIdFeature>();
        Assert.NotNull(streamIdFeature1);
        Assert.Equal(1, streamIdFeature1.StreamId);
        Assert.Equal("/path1", context1.Get<IHttpRequestFeature>()?.Path);

        var context3 = ops.Requests[1];
        var streamIdFeature3 = context3.Get<IHttpStreamIdFeature>();
        Assert.NotNull(streamIdFeature3);
        Assert.Equal(3, streamIdFeature3.StreamId);
        Assert.Equal("/path3", context3.Get<IHttpRequestFeature>()?.Path);

        // Now respond to stream 3 first
        transport.TakeWrittenBytes();
        var responseContext3 = ServerTestContext.CreateStreamResponse(streamId: 3);
        sm.OnResponse(responseContext3);

        // Verify HEADERS frame for stream 3 was emitted
        var decoder = new FrameDecoder();
        var frames3 = decoder.DecodeAll(new ReadOnlySequence<byte>(transport.WrittenMemory), out _).ToList();
        Assert.Contains(frames3, f => f is HeadersFrame { StreamId: 3 });

        // Now respond to stream 1
        transport.TakeWrittenBytes();
        var responseContext1 = ServerTestContext.CreateStreamResponse(streamId: 1);
        sm.OnResponse(responseContext1);

        // Verify HEADERS frame for stream 1 was emitted
        var frames1 = decoder.DecodeAll(new ReadOnlySequence<byte>(transport.WrittenMemory), out _).ToList();
        Assert.Contains(frames1, f => f is HeadersFrame { StreamId: 1 });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5")]
    public void Stream_IDs_should_preserve_request_response_correlation_across_interleaved_processing()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        // Send three requests on streams 1, 3, 5
        var transport = sm.ConnectTransport(ops: ops);
        for (var streamId = 1; streamId <= 5; streamId += 2)
        {
            var headerBlock = EncodeHeaders("GET", $"/path{streamId}", "example.com");
            var headersFrameData = BuildHeadersFrame(streamId, headerBlock, endStream: true, endHeaders: true);

            transport.FeedMore(sm, ops, headersFrameData);
        }

        Assert.Equal(3, ops.Requests.Count);

        // Verify each request has correct stream ID and path
        for (var i = 0; i < ops.Requests.Count; i++)
        {
            var context = ops.Requests[i];
            var expectedStreamId = 1 + (i * 2);
            var expectedPath = $"/path{expectedStreamId}";

            var streamIdFeature = context.Get<IHttpStreamIdFeature>();
            Assert.NotNull(streamIdFeature);
            Assert.Equal(expectedStreamId, streamIdFeature.StreamId);
            Assert.Equal(expectedPath, context.Get<IHttpRequestFeature>()?.Path);
        }

        // Respond in reverse order (5, 3, 1) and verify correct stream IDs are used
        var responseOrder = new[] { 2, 1, 0 };
        var decoder = new FrameDecoder();

        foreach (var idx in responseOrder)
        {
            var reqContext = ops.Requests[idx];
            var reqStreamIdFeature = reqContext.Get<IHttpStreamIdFeature>();
            var reqStreamId = reqStreamIdFeature?.StreamId ?? 0;

            transport.TakeWrittenBytes();
            var context = ServerTestContext.CreateStreamResponse(streamId: reqStreamId);
            sm.OnResponse(context);

            // Find HEADERS frame in transport output
            var frames = decoder.DecodeAll(new ReadOnlySequence<byte>(transport.WrittenMemory), out _).ToList();
            var foundCorrectStreamId = frames.OfType<HeadersFrame>().Any(h => h.StreamId == reqStreamId);

            Assert.True(foundCorrectStreamId,
                $"Expected HEADERS frame for stream {reqStreamId} to be emitted");
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5")]
    public void Concurrent_streams_should_maintain_independent_state()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new GaudiServerOptions().ToHttp2Options(), ops);

        // Send multiple requests without waiting for responses
        var headerBlock1 = EncodeHeaders("GET", "/");
        var headerBlock2 = EncodeHeaders("POST", "/submit");
        var headerBlock3 = EncodeHeaders("GET", "/status");

        var headersData1 = BuildHeadersFrame(1, headerBlock1, endStream: true, endHeaders: true);
        var headersData2 = BuildHeadersFrame(3, headerBlock2, endStream: true, endHeaders: true);
        var headersData3 = BuildHeadersFrame(5, headerBlock3, endStream: true, endHeaders: true);

        var transport = sm.ConnectTransport(initialData: headersData1, ops: ops);
        transport.FeedMore(sm, ops, headersData2);
        transport.FeedMore(sm, ops, headersData3);

        // All three requests should have been emitted
        Assert.Equal(3, ops.Requests.Count);

        // Verify each has correct stream ID
        var streamIds = ops.Requests
            .Select(ctx =>
            {
                var feature = ctx.Get<IHttpStreamIdFeature>();
                return (int)(feature?.StreamId ?? 0);
            })
            .OrderBy(id => id)
            .ToArray();

        Assert.Equal(new[] { 1, 3, 5 }, streamIds);

        // Verify paths match stream order
        Assert.Equal("/", ops.Requests[0].Get<IHttpRequestFeature>()?.Path);
        Assert.Equal("/submit", ops.Requests[1].Get<IHttpRequestFeature>()?.Path);
        Assert.Equal("/status", ops.Requests[2].Get<IHttpRequestFeature>()?.Path);
    }
}