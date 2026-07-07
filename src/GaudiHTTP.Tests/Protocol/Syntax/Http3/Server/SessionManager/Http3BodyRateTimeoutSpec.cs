using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

public sealed class Http3BodyRateTimeoutSpec
{


    private static byte[] BuildDataFrameBytes(int size)
    {
        using var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(size);
        var df = new DataFrame(owner, size);
        var buf = new byte[df.SerializedSize];
        var span = buf.AsSpan();
        df.WriteTo(ref span);
        return buf;
    }

    private static Http3ServerSessionManager CreateSM(FakeServerOps ops)
    {
        return new Http3ServerSessionManager(ServerOptionDefaults.Http3(), ops);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void First_DATA_frame_should_schedule_data_rate_check()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        const long streamId = 4;

        // Build HEADERS
        var headerBytes = ServerOptionDefaults.BuildHttp3Request("POST", "/upload");

        // Open stream
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));

        // Send HEADERS (no StreamReadCompleted yet)
        var headerBuffer = WireBuffer.Rent(headerBytes.Length);
        headerBytes.CopyTo(headerBuffer.FullMemory.Span);
        headerBuffer.Length = headerBytes.Length;
        sm.DecodeClientData(MultiplexedData.Rent(headerBuffer, streamId));

        // No request emitted yet (no StreamReadCompleted)
        Assert.Empty(ops.Requests);

        // Clear any timers from header processing
        ops.ScheduledTimers.Clear();

        // Build and send DATA frame
        var dataBytes = BuildDataFrameBytes(100);
        var dataBuffer = WireBuffer.Rent(dataBytes.Length);
        dataBytes.CopyTo(dataBuffer.FullMemory.Span);
        dataBuffer.Length = dataBytes.Length;
        sm.DecodeClientData(MultiplexedData.Rent(dataBuffer, streamId));

        // data-rate-check timer should now be scheduled
        Assert.True(ops.ScheduledTimers.Any(t => t.Name == "data-rate-check"),
            "data-rate-check timer should be scheduled after first DATA frame");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Headers_timeout_should_be_cancelled_on_successful_decode()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        const long streamId = 8;

        // Build HEADERS
        var headerBytes = ServerOptionDefaults.BuildHttp3Request("GET", "/");

        // Open stream
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));

        // Send HEADERS
        var headerBuffer = WireBuffer.Rent(headerBytes.Length);
        headerBytes.CopyTo(headerBuffer.FullMemory.Span);
        headerBuffer.Length = headerBytes.Length;
        sm.DecodeClientData(MultiplexedData.Rent(headerBuffer, streamId));

        // With capacity=0, QPACK decodes immediately, so headers-timeout is never scheduled.
        // Instead, FlushPendingRequest cancels it preemptively.

        // Send StreamReadCompleted to flush the pending request
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));

        // Request should now be emitted
        Assert.Single(ops.Requests);

        // The timeout should have been cancelled
        var timerName = string.Concat("headers-timeout:", streamId.ToString());
        Assert.Contains(timerName, ops.CancelledTimers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void StreamReadCompleted_without_body_should_emit_request_with_empty_content()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        const long streamId = 12;

        // Build HEADERS
        var headerBytes = ServerOptionDefaults.BuildHttp3Request("GET", "/");

        // Open stream
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));

        // Send HEADERS
        var headerBuffer = WireBuffer.Rent(headerBytes.Length);
        headerBytes.CopyTo(headerBuffer.FullMemory.Span);
        headerBuffer.Length = headerBytes.Length;
        sm.DecodeClientData(MultiplexedData.Rent(headerBuffer, streamId));

        // Send StreamReadCompleted (no DATA frames)
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));

        // Request should be emitted with empty content
        Assert.Single(ops.Requests);
        var context = ops.Requests[0];

        var requestFeature = context.Get<IHttpRequestFeature>() as GaudiHttpRequestFeature;
        Assert.NotNull(requestFeature);
        Assert.Equal("GET", requestFeature.Method);
        Assert.Equal("https", requestFeature.Scheme);
        Assert.Equal("localhost", requestFeature.ExtractedHost);
        Assert.Equal("/", requestFeature.Path);
    }
}