using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Protocol.Syntax.Http3.Server;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

/// <summary>
/// Regression spec for the HTTP/3 inbound DATA frame buffer leak: decoded frames own a
/// MemoryPool rental that must be disposed after handling, otherwise the pool drains and
/// every upload allocates its full body size in fresh arrays (benchmark showed 1.27 MB
/// allocated per 1 MB upload vs Kestrel's 87 KB).
/// </summary>
public sealed class Http3DataFrameBufferReleaseSpec
{
    private const int ChunkSize = 16 * 1024;
    private const int ChunksPerRequest = 32;
    private const int BodySize = ChunkSize * ChunksPerRequest;

    private static Http3ConnectionOptions DefaultConnectionOptions() => new()
    {
        Limits = new ResolvedServerLimits(
            MaxRequestBodySize: 30 * 1024 * 1024,
            KeepAliveTimeout: TimeSpan.FromSeconds(130),
            RequestHeadersTimeout: TimeSpan.FromSeconds(30),
            MinRequestBodyDataRate: 240,
            MinRequestBodyDataRateGracePeriod: TimeSpan.FromSeconds(5),
            MinResponseDataRate: 240,
            MinResponseDataRateGracePeriod: TimeSpan.FromSeconds(5)),
        MaxConcurrentStreams = 100,
        MaxHeaderListSize = 32 * 1024,
        MaxHeaderCount = 100,
        QpackMaxTableCapacity = 0,
        QpackBlockedStreams = 0,
        MaxResponseBufferSize = 64 * 1024,
        ResponseBodyChunkSize = 16 * 1024,
        BodyConsumptionTimeout = TimeSpan.FromSeconds(30),
        UseHuffman = true,
    };

    private static byte[] BuildRequestHeaders()
    {
        var tableSync = new QpackTableSync(0, 0, 0, 0);
        var headers = new List<(string, string)>
        {
            (":method", "POST"),
            (":path", "/upload"),
            (":scheme", "https"),
            (":authority", "localhost"),
        };
        var headerBlock = tableSync.Encoder.Encode(headers);
        var frame = new HeadersFrame(headerBlock);
        var buf = new byte[frame.SerializedSize];
        var span = buf.AsSpan();
        frame.WriteTo(ref span);
        return buf;
    }

    private static byte[] BuildDataFrameBytes(ReadOnlyMemory<byte> content)
    {
        var frame = new DataFrame(content);
        var buf = new byte[frame.SerializedSize];
        var span = buf.AsSpan();
        frame.WriteTo(ref span);
        return buf;
    }

    private static void Feed(Http3ServerSessionManager sm, byte[] wireBytes, long streamId)
    {
        var buffer = TransportBuffer.Rent(wireBytes.Length);
        wireBytes.CopyTo(buffer.FullMemory.Span);
        buffer.Length = wireBytes.Length;
        sm.DecodeClientData(new MultiplexedData(buffer, streamId));
    }

    private static async Task<long> SendUploadAndDrainBody(
        Http3ServerSessionManager sm, FakeServerOps ops, long streamId,
        byte[] headerBytes, byte[][] dataFrames, byte[] drainBuffer, byte? expectedPattern)
    {
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));
        Feed(sm, headerBytes, streamId);
        foreach (var dataFrame in dataFrames)
        {
            Feed(sm, dataFrame, streamId);
        }
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));

        var requestFeature = ops.Requests[^1].Get<IHttpRequestFeature>();
        Assert.NotNull(requestFeature);
        var body = requestFeature.Body;
        Assert.NotNull(body);

        long total = 0;
        int read;
        while ((read = await body.ReadAsync(drainBuffer)) > 0)
        {
            if (expectedPattern is { } pattern)
            {
                for (var i = 0; i < read; i++)
                {
                    if (drainBuffer[i] != pattern)
                    {
                        Assert.Fail($"Body byte at offset {total + i} was 0x{drainBuffer[i]:X2}, expected 0x{pattern:X2}.");
                    }
                }
            }
            total += read;
        }

        return total;
    }

    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public async Task Upload_body_should_round_trip_intact()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(DefaultConnectionOptions(), ops);

        var headerBytes = BuildRequestHeaders();
        var chunk = new byte[ChunkSize];
        Array.Fill(chunk, (byte)0xAB);
        var dataFrames = new byte[ChunksPerRequest][];
        for (var i = 0; i < ChunksPerRequest; i++)
        {
            dataFrames[i] = BuildDataFrameBytes(chunk);
        }

        var drainBuffer = new byte[64 * 1024];
        var total = await SendUploadAndDrainBody(sm, ops, 0, headerBytes, dataFrames, drainBuffer, 0xAB);

        Assert.Equal(BodySize, total);
    }

    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public async Task Repeated_uploads_should_not_allocate_proportional_to_body_size()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(DefaultConnectionOptions(), ops);

        var headerBytes = BuildRequestHeaders();
        var chunk = new byte[ChunkSize];
        Array.Fill(chunk, (byte)0x5C);
        var dataFrames = new byte[ChunksPerRequest][];
        for (var i = 0; i < ChunksPerRequest; i++)
        {
            dataFrames[i] = BuildDataFrameBytes(chunk);
        }

        var drainBuffer = new byte[64 * 1024];
        long streamId = 0;

        // Warm up pools so the measured phase reflects steady state.
        for (var i = 0; i < 3; i++)
        {
            await SendUploadAndDrainBody(sm, ops, streamId, headerBytes, dataFrames, drainBuffer, null);
            streamId += 4;
        }

        const int measuredRequests = 8;
        const long totalBodyBytes = (long)measuredRequests * BodySize;

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < measuredRequests; i++)
        {
            await SendUploadAndDrainBody(sm, ops, streamId, headerBytes, dataFrames, drainBuffer, null);
            streamId += 4;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // With pooled DATA frame buffers correctly returned, steady-state allocations are a
        // small fraction of the body. Pre-fix the leak forced ~1x the body size in fresh arrays.
        Assert.True(allocated < totalBodyBytes / 4,
            $"Allocated {allocated:N0} bytes for {totalBodyBytes:N0} bytes of upload body — " +
            "DATA frame pool rentals are not being returned.");
    }
}
