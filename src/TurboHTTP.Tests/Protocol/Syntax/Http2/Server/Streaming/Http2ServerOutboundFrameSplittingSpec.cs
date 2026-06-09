using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.Streaming;

public sealed class Http2ServerOutboundFrameSplittingSpec
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

    private static byte[] BuildSettingsFrameWithMaxFrameSize(uint maxFrameSize)
    {
        const int frameHeaderSize = 9;
        const int paramSize = 6;
        var frame = new byte[frameHeaderSize + paramSize];

        frame[0] = 0;
        frame[1] = 0;
        frame[2] = paramSize;
        frame[3] = (byte)FrameType.Settings;
        frame[4] = 0;

        var key = (ushort)SettingsParameter.MaxFrameSize;
        frame[9] = (byte)(key >> 8);
        frame[10] = (byte)key;
        frame[11] = (byte)(maxFrameSize >> 24);
        frame[12] = (byte)(maxFrameSize >> 16);
        frame[13] = (byte)(maxFrameSize >> 8);
        frame[14] = (byte)maxFrameSize;

        return frame;
    }

    private static byte[] BuildWindowUpdateFrame(int streamId, uint increment)
    {
        const int frameHeaderSize = 9;
        const int windowUpdateSize = 4;
        var frame = new byte[frameHeaderSize + windowUpdateSize];

        frame[0] = 0;
        frame[1] = 0;
        frame[2] = windowUpdateSize;
        frame[3] = (byte)FrameType.WindowUpdate;

        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;

        var incValue = increment & 0x7FFFFFFF;
        frame[9] = (byte)(incValue >> 24);
        frame[10] = (byte)(incValue >> 16);
        frame[11] = (byte)(incValue >> 8);
        frame[12] = (byte)incValue;

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

    private static void DecodeFramesAsStream(Http2ServerStateMachine sm, byte[] frameData)
    {
        var buffer = TransportBuffer.Rent(frameData.Length);
        frameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = frameData.Length;
        sm.DecodeClientData(TransportData.Rent(buffer));
    }

    private static List<Http2Frame> ExtractFrames(List<ITransportOutbound> outbound, int startIndex = 0)
    {
        var frames = new List<Http2Frame>();
        var decoder = new FrameDecoder();

        for (var i = startIndex; i < outbound.Count; i++)
        {
            if (outbound[i] is TransportData td)
            {
                var decodedFrames = decoder.Decode(td.Buffer);
                frames.AddRange(decodedFrames);
            }
        }

        return frames;
    }

    private static Http2ServerStateMachine CreateSmWithClientMaxFrameSize(
        FakeServerOps ops, uint clientMaxFrameSize, int connectionWindow = 1024 * 1024)
    {
        var sm = new Http2ServerStateMachine(new TurboServerOptions().ToHttp2Options(), ops);
        sm.PreStart();

        var settingsFrame = BuildSettingsFrameWithMaxFrameSize(clientMaxFrameSize);
        DecodeFramesAsStream(sm, settingsFrame);

        if (connectionWindow > 65535)
        {
            var connWindowUpdate = BuildWindowUpdateFrame(0, (uint)(connectionWindow - 65535));
            DecodeFramesAsStream(sm, connWindowUpdate);
        }

        ops.Outbound.Clear();
        return sm;
    }

    private static IFeatureCollection SendGetAndWriteBufferedBody(
        Http2ServerStateMachine sm, FakeServerOps ops, int streamId, int bodySize)
    {
        var headerBlock = EncodeHeaders("GET", "/large", "example.com");
        var headersFrameData = BuildHeadersFrame(streamId, headerBlock, endStream: true, endHeaders: true);
        DecodeFramesAsStream(sm, headersFrameData);

        var features = ops.Requests[^1];
        var responseFeature = features.Get<IHttpResponseFeature>()!;
        responseFeature.StatusCode = 200;
        responseFeature.Headers["Content-Length"] = bodySize.ToString();

        var bodyFeature = features.Get<IHttpResponseBodyFeature>()!;
        var body = new byte[bodySize];
        for (var i = 0; i < body.Length; i++)
        {
            body[i] = (byte)(i % 251);
        }

        var writer = bodyFeature.Writer;
        var mem = writer.GetMemory(bodySize);
        body.CopyTo(mem);
        writer.Advance(bodySize);

        return features;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.2")]
    public void OnResponse_buffered_body_should_split_frames_by_max_frame_size()
    {
        var ops = new FakeServerOps();
        const uint clientMaxFrameSize = 16 * 1024;
        const int bodySize = 48 * 1024;
        var sm = CreateSmWithClientMaxFrameSize(ops, clientMaxFrameSize, connectionWindow: bodySize + 65535);

        var features = SendGetAndWriteBufferedBody(sm, ops, streamId: 1, bodySize);
        var streamWindowUpdate = BuildWindowUpdateFrame(1, (uint)bodySize);
        DecodeFramesAsStream(sm, streamWindowUpdate);

        ops.Outbound.Clear();
        sm.OnResponse(features);

        var frames = ExtractFrames(ops.Outbound);
        var dataFrames = frames.OfType<DataFrame>().ToList();

        Assert.True(dataFrames.Count >= 3, $"Expected at least 3 DATA frames for {bodySize} bytes at {clientMaxFrameSize} max frame size, got {dataFrames.Count}");

        foreach (var df in dataFrames)
        {
            Assert.True(df.Data.Length <= (int)clientMaxFrameSize,
                $"DATA frame payload {df.Data.Length} exceeds client MAX_FRAME_SIZE {clientMaxFrameSize}");
        }

        var totalDataBytes = dataFrames.Sum(df => df.Data.Length);
        Assert.Equal(bodySize, totalDataBytes);

        Assert.True(dataFrames[^1].EndStream);
        for (var i = 0; i < dataFrames.Count - 1; i++)
        {
            Assert.False(dataFrames[i].EndStream);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.2")]
    public void OnResponse_buffered_body_should_respect_custom_client_max_frame_size()
    {
        var ops = new FakeServerOps();
        const uint clientMaxFrameSize = 32 * 1024;
        const int bodySize = 96 * 1024;
        var sm = CreateSmWithClientMaxFrameSize(ops, clientMaxFrameSize, connectionWindow: bodySize + 65535);

        var features = SendGetAndWriteBufferedBody(sm, ops, streamId: 1, bodySize);
        var streamWindowUpdate = BuildWindowUpdateFrame(1, (uint)bodySize);
        DecodeFramesAsStream(sm, streamWindowUpdate);

        ops.Outbound.Clear();
        sm.OnResponse(features);

        var frames = ExtractFrames(ops.Outbound);
        var dataFrames = frames.OfType<DataFrame>().ToList();

        foreach (var df in dataFrames)
        {
            Assert.True(df.Data.Length <= (int)clientMaxFrameSize,
                $"DATA frame payload {df.Data.Length} exceeds client MAX_FRAME_SIZE {clientMaxFrameSize}");
        }

        var totalDataBytes = dataFrames.Sum(df => df.Data.Length);
        Assert.Equal(bodySize, totalDataBytes);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void DrainOutboundBuffer_should_partial_send_when_window_is_smaller_than_chunk()
    {
        var ops = new FakeServerOps();
        const uint clientMaxFrameSize = 16 * 1024;
        const int bodySize = 48 * 1024;
        var sm = CreateSmWithClientMaxFrameSize(ops, clientMaxFrameSize, connectionWindow: bodySize + 65535);

        var features = SendGetAndWriteBufferedBody(sm, ops, streamId: 1, bodySize);

        ops.Outbound.Clear();
        sm.OnResponse(features);

        var framesBeforeWindowUpdate = ExtractFrames(ops.Outbound);
        var dataBeforeWindowUpdate = framesBeforeWindowUpdate.OfType<DataFrame>().ToList();

        var totalSentBefore = dataBeforeWindowUpdate.Sum(df => df.Data.Length);
        Assert.True(totalSentBefore <= 65535, "Should not exceed initial send window of 65535");
        Assert.True(totalSentBefore > 0, "Should send at least some data within the initial window");

        ops.Outbound.Clear();
        var windowUpdate = BuildWindowUpdateFrame(1, (uint)bodySize);
        DecodeFramesAsStream(sm, windowUpdate);

        var framesAfterWindowUpdate = ExtractFrames(ops.Outbound);
        var dataAfterWindowUpdate = framesAfterWindowUpdate.OfType<DataFrame>().ToList();
        var totalSentAfter = dataAfterWindowUpdate.Sum(df => df.Data.Length);

        Assert.Equal(bodySize, totalSentBefore + totalSentAfter);
    }
}
