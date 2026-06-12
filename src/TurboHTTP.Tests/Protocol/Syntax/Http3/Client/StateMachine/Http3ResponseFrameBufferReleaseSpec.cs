using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client.StateMachine;

/// <summary>
/// Regression spec for the HTTP/3 inbound frame buffer leak on the client: decoded
/// HEADERS/DATA frames own a MemoryPool rental that must be disposed after response
/// assembly, otherwise the pool drains and every response body allocates fresh arrays.
/// </summary>
public sealed class Http3ResponseFrameBufferReleaseSpec
{
    private const int ChunkSize = 16 * 1024;
    private const int ChunksPerResponse = 8;
    private const int BodySize = ChunkSize * ChunksPerResponse;

    private readonly FakeClientOps _clientOps = new();
    private readonly QpackTableSync _tableSync = new(0, 4 * 1024, 100, null);

    private Http3ClientStateMachine CreateMachine()
        => new(new TurboClientOptions(), _clientOps);

    private TransportBuffer BuildHeadersBuffer()
    {
        var headersFrame = new HeadersFrame(_tableSync.Encoder.Encode([(":status", "200")]));
        var buf = TransportBuffer.Rent(headersFrame.SerializedSize);
        var span = buf.FullMemory.Span;
        headersFrame.WriteTo(ref span);
        buf.Length = headersFrame.SerializedSize;
        return buf;
    }

    private static TransportBuffer BuildDataBuffer(ReadOnlyMemory<byte> chunk)
    {
        var dataFrame = new DataFrame(chunk);
        var buf = TransportBuffer.Rent(dataFrame.SerializedSize);
        var span = buf.FullMemory.Span;
        dataFrame.WriteTo(ref span);
        buf.Length = dataFrame.SerializedSize;
        return buf;
    }

    private long ReceiveResponseAndDrainBody(
        Http3ClientStateMachine sm, long streamId, byte[] chunk, byte[] drainBuffer)
    {
        sm.DecodeServerData(new MultiplexedData(BuildHeadersBuffer(), streamId));
        for (var i = 0; i < ChunksPerResponse; i++)
        {
            sm.DecodeServerData(new MultiplexedData(BuildDataBuffer(chunk), streamId));
        }
        sm.DecodeServerData(new StreamReadCompleted(streamId));

        var response = _clientOps.Responses[^1];
        var body = response.Content.ReadAsStream(TestContext.Current.CancellationToken);

        long total = 0;
        int read;
        while ((read = body.Read(drainBuffer)) > 0)
        {
            total += read;
        }

        return total;
    }

    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9114-7.2.1")]
    public void Repeated_responses_should_not_allocate_proportional_to_body_size()
    {
        var sm = CreateMachine();
        var chunk = new byte[ChunkSize];
        Array.Fill(chunk, (byte)0x3E);
        var drainBuffer = new byte[64 * 1024];
        long streamId = 0;

        // Warm up pools so the measured phase reflects steady state.
        for (var i = 0; i < 3; i++)
        {
            var total = ReceiveResponseAndDrainBody(sm, streamId, chunk, drainBuffer);
            Assert.Equal(BodySize, total);
            streamId += 4;
        }

        const int measuredResponses = 16;
        const long totalBodyBytes = (long)measuredResponses * BodySize;

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < measuredResponses; i++)
        {
            ReceiveResponseAndDrainBody(sm, streamId, chunk, drainBuffer);
            streamId += 4;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < totalBodyBytes / 16,
            $"Allocated {allocated:N0} bytes for {totalBodyBytes:N0} bytes of response body — " +
            "frame pool rentals are not being returned.");
    }
}
