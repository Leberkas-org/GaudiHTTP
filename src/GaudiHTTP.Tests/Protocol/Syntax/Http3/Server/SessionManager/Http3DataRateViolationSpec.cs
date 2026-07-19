using Microsoft.Extensions.Time.Testing;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Qpack;
using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

public sealed class Http3DataRateViolationSpec
{
    private static byte[] BuildDataFrameBytes(int size)
    {
        using var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(size);
        var df = new DataFrame(owner.Memory[..size]);
        var buf = new byte[df.SerializedSize];
        var span = buf.AsSpan();
        df.WriteTo(ref span);
        return buf;
    }

    private static Http3ConnectionOptions OptionsWithRequestRate(double minRate, TimeSpan grace) =>
        ServerOptionDefaults.Http3() with { Limits = ServerOptionDefaults.Limits() with { MinRequestBodyDataRate = minRate, MinRequestBodyDataRateGracePeriod = grace, MinResponseDataRate = 0 } };

    private static void Send(Http3ServerSessionManager sm, long streamId, byte[] bytes)
    {
        var buffer = WireBuffer.Rent(bytes.Length);
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
        Send(sm, streamId, ServerOptionDefaults.BuildHttp3Request("POST", "/upload"));
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
