using Microsoft.Extensions.Time.Testing;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Qpack;
using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

public sealed class Http3DataRateViolationSpec
{
    private static byte[] BuildRequest(string method, string path)
    {
        var tableSync = new QpackTableSync(0, 0, 0, 0);
        var headers = new List<(string, string)>
        {
            (":method", method),
            (":path", path),
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

    private static byte[] BuildDataFrameBytes(int size)
    {
        using var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(size);
        var df = new DataFrame(owner, size);
        var buf = new byte[df.SerializedSize];
        var span = buf.AsSpan();
        df.WriteTo(ref span);
        return buf;
    }

    private static Http3ConnectionOptions OptionsWithRequestRate(double minRate, TimeSpan grace) => new()
    {
        Limits = new ResolvedServerLimits(
            MaxRequestBodySize: 30 * 1024 * 1024,
            KeepAliveTimeout: TimeSpan.FromSeconds(130),
            RequestHeadersTimeout: TimeSpan.FromSeconds(30),
            MinRequestBodyDataRate: minRate,
            MinRequestBodyDataRateGracePeriod: grace,
            MinResponseDataRate: 0,
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

    private static void Send(Http3ServerSessionManager sm, long streamId, byte[] bytes)
    {
        var buffer = TransportBuffer.Rent(bytes.Length);
        bytes.CopyTo(buffer.FullMemory.Span);
        buffer.Length = bytes.Length;
        sm.DecodeClientData(MultiplexedData.Rent(buffer, streamId));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Slow_request_body_should_reset_stream_after_grace_with_injected_clock()
    {
        var clock = new FakeTimeProvider();
        var ops = new FakeServerOps();
        var sm = new Http3ServerSessionManager(OptionsWithRequestRate(1000, TimeSpan.FromSeconds(1)), ops, clock);

        const long streamId = 4;

        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId), StreamDirection.Bidirectional));
        Send(sm, streamId, BuildRequest("POST", "/upload"));
        // A tiny DATA frame arrives, then the upload stalls (no StreamReadCompleted).
        Send(sm, streamId, BuildDataFrameBytes(5));

        clock.Advance(TimeSpan.FromMilliseconds(600));
        sm.CheckDataRates();
        Assert.DoesNotContain(ops.Outbound, o => o is ResetStream);

        // 5 bytes over 1700ms = ~2.9 bytes/sec << 1000; grace (1s) expired → ResetStream.
        clock.Advance(TimeSpan.FromMilliseconds(1100));
        sm.CheckDataRates();
        Assert.Contains(ops.Outbound, o => o is ResetStream);
    }
}
