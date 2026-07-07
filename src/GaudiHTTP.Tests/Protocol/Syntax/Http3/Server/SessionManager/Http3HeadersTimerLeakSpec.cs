using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http3;
using GaudiHTTP.Protocol.Syntax.Http3.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

/// <summary>
/// Regression tests for the HeadersTimeout timer leak in Http3ServerSessionManager.CloseStream().
/// Previously, CloseStream() cancelled BodyConsumptionTimerKey but omitted HeadersTimeoutTimerKey,
/// leaving the headers-timeout:&lt;id&gt; timer armed after the stream was closed.
/// </summary>
public sealed class Http3HeadersTimerLeakSpec
{

    private static Http3ServerSessionManager CreateSm(FakeServerOps ops)
    {
        return new Http3ServerSessionManager(ServerOptionDefaults.Http3() with { UseHuffman = false }, ops);
    }

    private static void OpenAndFlushStream(Http3ServerSessionManager sm, long streamId,
        string method = "GET", string path = "/")
    {
        var headersBytes = ServerOptionDefaults.BuildHttp3Request(method, path);

        sm.DecodeClientData(new ServerStreamAccepted(
            StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));

        var buffer = WireBuffer.Rent(headersBytes.Length);
        headersBytes.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersBytes.Length;
        sm.DecodeClientData(MultiplexedData.Rent(buffer, streamId));

        // StreamReadCompleted makes the stream fully registered
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EmitRstStream_should_cancel_headers_timeout_timer()
    {
        // Regression: CloseStream() previously did NOT cancel HeadersTimeoutTimerKey.
        // When the server emits RST_STREAM to abort a stream, the headers-timeout:<id>
        // timer was left armed, leaking a timer handle.
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);

        const long streamId = 4;
        OpenAndFlushStream(sm, streamId);

        Assert.Single(ops.Requests);
        ops.CancelledTimers.Clear();

        // Server RST_STREAM — CloseStream must cancel the headers-timeout timer
        sm.EmitRstStream(streamId, ErrorCode.GeneralProtocolError);

        Assert.Contains(ops.CancelledTimers, name => name.StartsWith("headers-timeout:" + streamId));
        Assert.Equal(0, sm.ActiveStreamCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void EmitRstStream_should_cancel_both_body_and_headers_timers()
    {
        // Combined guard: both BodyConsumptionTimerKey and HeadersTimeoutTimerKey must be
        // cancelled — protects against partial-fix regressions.
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);

        const long streamId = 8;
        OpenAndFlushStream(sm, streamId);

        Assert.Single(ops.Requests);
        ops.CancelledTimers.Clear();

        sm.EmitRstStream(streamId, ErrorCode.GeneralProtocolError);

        Assert.Contains(ops.CancelledTimers, name => name.StartsWith("headers-timeout:" + streamId));
        Assert.Contains(ops.CancelledTimers, name => name.StartsWith("body-consumption:" + streamId));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void CloseStream_on_multiple_streams_should_cancel_respective_timer_keys()
    {
        // Each stream's CloseStream call must cancel that stream's own timer keys,
        // not a shared key — verifies per-stream key naming.
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);

        const long streamId4 = 4;
        const long streamId8 = 8;

        OpenAndFlushStream(sm, streamId4);
        OpenAndFlushStream(sm, streamId8, "POST", "/upload");

        Assert.Equal(2, ops.Requests.Count);
        ops.CancelledTimers.Clear();

        sm.EmitRstStream(streamId4, ErrorCode.GeneralProtocolError);
        Assert.Contains(ops.CancelledTimers, name => name == "headers-timeout:" + streamId4);
        Assert.DoesNotContain(ops.CancelledTimers, name => name == "headers-timeout:" + streamId8);

        ops.CancelledTimers.Clear();

        sm.EmitRstStream(streamId8, ErrorCode.GeneralProtocolError);
        Assert.Contains(ops.CancelledTimers, name => name == "headers-timeout:" + streamId8);
        Assert.DoesNotContain(ops.CancelledTimers, name => name == "headers-timeout:" + streamId4);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void FlushPendingRequest_should_cancel_headers_timeout_timer()
    {
        // FlushPendingRequest is the path for StreamReadCompleted and StreamClosed events.
        // It must also cancel the headers-timeout timer when finalizing a stream.
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);

        const long streamId = 12;
        var headersBytes = ServerOptionDefaults.BuildHttp3Request("GET", "/resource");

        sm.DecodeClientData(new ServerStreamAccepted(
            StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));

        var buffer = WireBuffer.Rent(headersBytes.Length);
        headersBytes.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersBytes.Length;
        sm.DecodeClientData(MultiplexedData.Rent(buffer, streamId));

        // Do NOT call StreamReadCompleted yet — stream is pending
        Assert.Empty(ops.Requests);

        ops.CancelledTimers.Clear();

        // StreamReadCompleted triggers FlushPendingRequest → OnCancelTimer for both keys
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));

        Assert.Contains(ops.CancelledTimers, name => name.StartsWith("headers-timeout:" + streamId));
        Assert.Single(ops.Requests);
    }
}
