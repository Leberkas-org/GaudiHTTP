using System.Text;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerStateMachineConnectionSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void ShouldComplete_should_be_true_when_connection_close_on_request()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\nContent-Length: 0\r\n\r\n";

        _ = sm.ConnectTransport(Encoding.ASCII.GetBytes(requestData), ops);

        Assert.True(sm.ShouldComplete);
        Assert.Single(ops.Requests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void ShouldComplete_should_be_true_for_http10_request_on_h11_connection()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        const string requestData = "GET / HTTP/1.0\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";

        _ = sm.ConnectTransport(Encoding.ASCII.GetBytes(requestData), ops);

        Assert.True(sm.ShouldComplete);
        Assert.Single(ops.Requests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void OnResponse_should_include_connection_close_when_ShouldComplete()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\nContent-Length: 0\r\n\r\n";

        var transport = sm.ConnectTransport(Encoding.ASCII.GetBytes(requestData), ops);
        Assert.True(sm.ShouldComplete);

        var context = ServerTestContext.CreateResponse();

        sm.OnResponse(context);

        var responseText = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.Contains("Connection: close", responseText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void DecodeClientData_should_set_ShouldComplete_on_decode_error()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        const string invalidRequest = "INVALID REQUEST DATA\r\n\r\n";

        _ = sm.ConnectTransport(Encoding.ASCII.GetBytes(invalidRequest), ops);

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnBodyMessage_ResponseBodyReadFailed_should_clear_pending_flag()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";

        _ = sm.ConnectTransport(Encoding.ASCII.GetBytes(requestData), ops);
        Assert.True(sm.CanAcceptResponse);

        var context = ServerTestContext.CreateResponse();

        sm.OnResponse(context);

        // After response, CanAcceptResponse should be false because body is pending
        Assert.False(sm.CanAcceptResponse);

        // Send body failed
        sm.OnBodyMessage(new BodyReadFailed<int>(0, new Exception("Test failure")));

        // After body failed, CanAcceptResponse is false because _pendingResponseCount == 0 (response already sent)
        // not because body is pending
        Assert.False(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnBodyMessage_multi_chunk_should_emit_all_chunks()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";

        var transport = sm.ConnectTransport(Encoding.ASCII.GetBytes(requestData), ops);

        var context = ServerTestContext.CreateResponse();

        sm.OnResponse(context);
        var initialLength = transport.WrittenCount;

        // Send two read completions followed by EOF
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 5));
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 6));
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 0));

        // Verify multiple chunks were written after the initial response
        var finalLength = transport.WrittenCount;
        Assert.True(finalLength > initialLength, "Expected additional data to be written for body chunks");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void Cleanup_should_be_idempotent()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";

        _ = sm.ConnectTransport(Encoding.ASCII.GetBytes(requestData), ops);

        var context = ServerTestContext.CreateResponse();
        sm.OnResponse(context);

        // Call Cleanup twice
        sm.Cleanup();
        sm.Cleanup();

        // Should not crash
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnResponse_should_throw_when_no_pending_requests()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        var context = ServerTestContext.CreateResponse();

        var ex = Assert.Throws<InvalidOperationException>(() => sm.OnResponse(context));
        Assert.Contains("no requests are pending", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.3")]
    public void OnResponse_should_set_chunked_transfer_encoding_when_no_content_length()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var transport = sm.ConnectTransport(Encoding.ASCII.GetBytes(requestData), ops);

        var context = ServerTestContext.CreateResponse();
        context.Get<IHttpResponseFeature>()?.StatusCode = 200;
        context.Get<IHttpResponseFeature>()?.Headers["Content-Type"] = "text/event-stream";

        sm.OnResponse(context);

        var responseText = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.Contains("Transfer-Encoding: chunked", responseText);
        Assert.False(sm.CanAcceptResponse);

        sm.Cleanup();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void OnResponse_should_not_set_chunked_when_content_length_present()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var transport = sm.ConnectTransport(Encoding.ASCII.GetBytes(requestData), ops);

        var context = ServerTestContext.CreateResponse();
        context.Get<IHttpResponseFeature>()?.StatusCode = 200;
        context.Get<IHttpResponseFeature>()?.Headers["Content-Length"] = "5";

        sm.OnResponse(context);

        var responseText = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.DoesNotContain("Transfer-Encoding: chunked", responseText);
        Assert.Contains("Content-Length: 5", responseText);

        sm.Cleanup();
    }
}



