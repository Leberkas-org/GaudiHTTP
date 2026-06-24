using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Protocol.Syntax.Http2.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server.Streaming;

public sealed class Http2ServerBatchedDataEmissionSpec
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

    private sealed record FrameExtractionResult(
        List<Http2Frame> AllFrames,
        List<DataFrame> DataFrames,
        int DataCarryingBufferCount);

    private static FrameExtractionResult ExtractFramesAndCountBuffers(
        List<ITransportOutbound> outbound, int startIndex = 0)
    {
        var allFrames = new List<Http2Frame>();
        var dataFrames = new List<DataFrame>();
        var dataBufferCount = 0;
        var decoder = new FrameDecoder();

        for (var i = startIndex; i < outbound.Count; i++)
        {
            if (outbound[i] is TransportData td)
            {
                var decoded = decoder.Decode(td.Buffer);
                var hasData = false;
                foreach (var frame in decoded)
                {
                    allFrames.Add(frame);
                    if (frame is DataFrame df)
                    {
                        dataFrames.Add(df);
                        hasData = true;
                    }
                }

                if (hasData)
                {
                    dataBufferCount++;
                }
            }
        }

        return new FrameExtractionResult(allFrames, dataFrames, dataBufferCount);
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
        writer.Complete();

        return features;
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_buffered_body_should_batch_data_frames_into_fewer_buffers()
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

        var result = ExtractFramesAndCountBuffers(ops.Outbound);

        Assert.Equal(3, result.DataFrames.Count);

        var totalDataBytes = result.DataFrames.Sum(df => df.Data.Length);
        Assert.Equal(bodySize, totalDataBytes);

        Assert.True(result.DataFrames[^1].EndStream);
        for (var i = 0; i < result.DataFrames.Count - 1; i++)
        {
            Assert.False(result.DataFrames[i].EndStream);
        }

        Assert.Equal(1, result.DataCarryingBufferCount);
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_single_frame_body_should_emit_one_buffer()
    {
        var ops = new FakeServerOps();
        const uint clientMaxFrameSize = 16 * 1024;
        const int bodySize = 2 * 1024;
        var sm = CreateSmWithClientMaxFrameSize(ops, clientMaxFrameSize, connectionWindow: bodySize + 65535);

        var features = SendGetAndWriteBufferedBody(sm, ops, streamId: 1, bodySize);
        var streamWindowUpdate = BuildWindowUpdateFrame(1, (uint)bodySize);
        DecodeFramesAsStream(sm, streamWindowUpdate);

        ops.Outbound.Clear();
        sm.OnResponse(features);

        var result = ExtractFramesAndCountBuffers(ops.Outbound);

        Assert.Single(result.DataFrames);
        Assert.Equal(bodySize, result.DataFrames[0].Data.Length);
        Assert.True(result.DataFrames[0].EndStream);
        Assert.Equal(1, result.DataCarryingBufferCount);
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_large_body_should_batch_many_frames_into_fewer_buffers()
    {
        var ops = new FakeServerOps();
        const uint clientMaxFrameSize = 16 * 1024;
        const int bodySize = 128 * 1024;
        var sm = CreateSmWithClientMaxFrameSize(ops, clientMaxFrameSize, connectionWindow: bodySize + 65535);

        var features = SendGetAndWriteBufferedBody(sm, ops, streamId: 1, bodySize);
        var streamWindowUpdate = BuildWindowUpdateFrame(1, (uint)bodySize);
        DecodeFramesAsStream(sm, streamWindowUpdate);

        ops.Outbound.Clear();
        sm.OnResponse(features);

        var result = ExtractFramesAndCountBuffers(ops.Outbound);

        Assert.Equal(8, result.DataFrames.Count);

        var offset = 0;
        foreach (var df in result.DataFrames)
        {
            for (var i = 0; i < df.Data.Length; i++)
            {
                Assert.Equal((byte)((offset + i) % 251), df.Data.Span[i]);
            }

            offset += df.Data.Length;
        }

        Assert.Equal(bodySize, offset);
        Assert.Equal(1, result.DataCarryingBufferCount);
    }
}
