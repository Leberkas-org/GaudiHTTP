using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Tests.Shared;
using static TurboHTTP.Protocol.Syntax.Http11.Server.Http11ServerStateMachine;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerStateMachineTimerSpec
{
    private static IFeatureCollection CreateResponseContext()
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return features;
    }

    private static TransportBuffer MakeBuffer(string raw)
    {
        var data = Encoding.ASCII.GetBytes(raw);
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.5")]
    public void OnTimerFired_request_headers_should_set_ShouldComplete()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions().ToHttp1Options(), new TurboServerOptions().ToHttp2Options(), ops);

        sm.OnTimerFired("request-headers");

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void OnTimerFired_keep_alive_should_set_ShouldComplete()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions().ToHttp1Options(), new TurboServerOptions().ToHttp2Options(), ops);

        sm.OnTimerFired("keep-alive");

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.5")]
    public void DecodeClientData_should_schedule_request_headers_timer()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions().ToHttp1Options(), new TurboServerOptions().ToHttp2Options(), ops);

        // Feed partial request data (no final \r\n\r\n) to trigger NeedMore state
        // This keeps the decoder in incomplete state, allowing timer scheduling
        var partialRequest = "GET / HTTP/1.1\r\nHost: localhost\r\n";
        var buffer = MakeBuffer(partialRequest);

        sm.DecodeClientData(TransportData.Rent(buffer));

        Assert.Contains(ops.ScheduledTimers, t => t.Name == "request-headers");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.5")]
    public void DecodeClientData_should_cancel_request_headers_timer_when_complete()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions().ToHttp1Options(), new TurboServerOptions().ToHttp2Options(), ops);

        // First, feed partial request to schedule timer
        var partialRequest = "GET / HTTP/1.1\r\nHost: localhost\r\n";
        var buffer1 = MakeBuffer(partialRequest);
        sm.DecodeClientData(TransportData.Rent(buffer1));

        // Then feed completion to cancel timer
        var completion = "\r\n";
        var buffer2 = MakeBuffer(completion);
        sm.DecodeClientData(TransportData.Rent(buffer2));

        Assert.Contains(ops.CancelledTimers, t => t == "request-headers");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void OnResponse_should_schedule_keep_alive_timer_after_204_body_completes()
    {
        var ops = new FakeServerOps();

        var sm = new Http11ServerStateMachine(new TurboServerOptions().ToHttp1Options(), new TurboServerOptions().ToHttp2Options(), ops);

        // Decode a complete request first
        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var buffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(buffer));

        // Verify we have a pending request
        Assert.Single(ops.Requests);
        Assert.True(sm.CanAcceptResponse);

        // Send a 204 No Content response (has EmptyContent automatically)
        var context = CreateResponseContext();

        sm.OnResponse(context);

        // Clear timers to isolate the keep-alive timer from request-headers timer
        var timersBeforeBodyComplete = ops.ScheduledTimers.ToList();

        // Complete the body (even though it's empty)
        sm.OnBodyMessage(new ResponseBodyReadComplete(0));

        // Check that keep-alive timer was scheduled after body completion
        var newTimers = ops.ScheduledTimers.Skip(timersBeforeBodyComplete.Count).ToList();
        Assert.Contains(newTimers, t => t.Name == "keep-alive");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnBodyMessage_complete_should_schedule_keep_alive_timer()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions().ToHttp1Options(), new TurboServerOptions().ToHttp2Options(), ops);

        // Decode a request
        var requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var buffer = MakeBuffer(requestData);
        sm.DecodeClientData(TransportData.Rent(buffer));

        // Send response with body
        var context = CreateResponseContext();

        sm.OnResponse(context);

        // Send body chunk and completion
        sm.OnBodyMessage(new ResponseBodyReadComplete(5));

        // Complete the body — this should schedule keep-alive timer
        sm.OnBodyMessage(new ResponseBodyReadComplete(0));

        Assert.Contains(ops.ScheduledTimers, t => t.Name == "keep-alive");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void DecodeClientData_should_schedule_body_read_timer_while_body_streaming()
    {
        var opts = new TurboServerOptions
        {
            Http1 =
            {
                BodyReadTimeout = TimeSpan.FromSeconds(5)
            }
        };
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(opts.ToHttp1Options(), new TurboServerOptions().ToHttp2Options(), ops);

        var req = "POST / HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: chunked\r\n\r\n";
        sm.DecodeClientData(TransportData.Rent(MakeBuffer(req)));

        Assert.Contains(ops.ScheduledTimers, t => t.Name == "body-read" && t.Delay == TimeSpan.FromSeconds(5));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void OnTimerFired_body_read_should_set_ShouldComplete()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions().ToHttp1Options(), new TurboServerOptions().ToHttp2Options(), ops);

        sm.OnTimerFired("body-read");

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void DecodeClientData_should_cancel_body_read_timer_when_body_completes()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions().ToHttp1Options(), new TurboServerOptions().ToHttp2Options(), ops);

        var head = "POST / HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: chunked\r\n\r\n";
        sm.DecodeClientData(TransportData.Rent(MakeBuffer(head)));
        Assert.Contains(ops.ScheduledTimers, t => t.Name == "body-read");

        var body = "5\r\nhello\r\n0\r\n\r\n";
        sm.DecodeClientData(TransportData.Rent(MakeBuffer(body)));

        Assert.Contains(ops.CancelledTimers, t => t == "body-read");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.5")]
    public void Cleanup_should_cancel_all_timers()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new TurboServerOptions().ToHttp1Options(), new TurboServerOptions().ToHttp2Options(), ops);

        // Decode a partial request to activate request-headers timer
        var partialRequest = "GET / HTTP/1.1\r\nHost: localhost\r\n";
        var buffer = MakeBuffer(partialRequest);
        sm.DecodeClientData(TransportData.Rent(buffer));

        Assert.Contains(ops.ScheduledTimers, t => t.Name == "request-headers");

        // Now call Cleanup — should cancel both timers
        sm.Cleanup();

        Assert.Contains(ops.CancelledTimers, t => t == "request-headers");
        Assert.Contains(ops.CancelledTimers, t => t == "keep-alive");
    }
}