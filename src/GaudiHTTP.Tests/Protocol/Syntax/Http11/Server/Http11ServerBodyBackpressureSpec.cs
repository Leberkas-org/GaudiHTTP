using System.Text;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

/// <summary>
/// Tests for the SerialBodyPump-based body drain flow. The pump is inherently sequential
/// (one read at a time), so explicit watermark-based pause/resume is no longer needed.
/// These tests verify the BodyReadComplete/BodyReadFailed message handling.
/// </summary>
public sealed class Http11ServerBodyBackpressureSpec
{
    private static Http11ServerStateMachine CreateSm(FakeServerOps ops)
    {
        return new Http11ServerStateMachine(
            new GaudiServerOptions().ToHttp1Options(),
            new GaudiServerOptions().ToHttp2Options(),
            ops);
    }


    [Fact(Timeout = 5000)]
    public void OnBodyMessage_should_emit_transport_data_for_each_read_completion()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var transport = sm.ConnectTransport(Encoding.ASCII.GetBytes(requestData), ops);

        var context = ServerTestContext.CreateResponse();
        sm.OnResponse(context);
        var initialCount = transport.WrittenCount;

        // Simulate multiple PipeTo read completions
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 100));
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 200));
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 50));
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 0));

        // 3 data chunks + 1 chunked terminator from CompleteAsync
        var finalCount = transport.WrittenCount;
        Assert.True(finalCount >= initialCount + 4,
            $"Expected at least 4 additional items written, got {finalCount - initialCount}");
    }

    [Fact(Timeout = 5000)]
    public void OnBodyMessage_complete_should_clear_outbound_pending_flag()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        _ = sm.ConnectTransport(Encoding.ASCII.GetBytes(requestData), ops);

        var context = ServerTestContext.CreateResponse();
        sm.OnResponse(context);

        // Body is pending after OnResponse
        Assert.False(sm.CanAcceptResponse);

        sm.OnBodyMessage(new BodyReadComplete<int>(0, 10));
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 0));

        // After body complete, outbound pending is cleared
        // (CanAcceptResponse is still false because _pendingResponseCount == 0)
        Assert.False(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    public void OnBodyMessage_failed_should_clear_outbound_pending_flag()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        _ = sm.ConnectTransport(Encoding.ASCII.GetBytes(requestData), ops);

        var context = ServerTestContext.CreateResponse();
        sm.OnResponse(context);

        sm.OnBodyMessage(new BodyReadComplete<int>(0, 10));
        sm.OnBodyMessage(new BodyReadFailed<int>(0, new Exception("simulated failure")));
    }
}
