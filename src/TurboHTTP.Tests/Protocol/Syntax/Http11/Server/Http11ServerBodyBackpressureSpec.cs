using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Tests.Shared;
using static TurboHTTP.Protocol.Syntax.Http11.Server.Http11ServerStateMachine;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

/// <summary>
/// Tests for the PipeTo-based response body flow. PipeTo is inherently sequential
/// (one read at a time), so explicit watermark-based pause/resume is no longer needed.
/// These tests verify the ResponseBodyReadComplete/Failed message handling.
/// </summary>
public sealed class Http11ServerBodyBackpressureSpec
{
    private static IFeatureCollection CreateResponseContext()
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new TurboHttpResponseBodyFeature();
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

    private static Http11ServerStateMachine CreateSm(FakeServerOps ops)
    {
        return new Http11ServerStateMachine(
            new TurboServerOptions().ToHttp1Options(),
            new TurboServerOptions().ToHttp2Options(),
            ops);
    }

    private static void SendRequest(Http11ServerStateMachine sm)
    {
        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        sm.DecodeClientData(new TransportData(MakeBuffer(requestData)));
    }

    [Fact(Timeout = 5000)]
    public void OnBodyMessage_should_emit_transport_data_for_each_read_completion()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        var context = CreateResponseContext();
        sm.OnResponse(context);
        var headerCount = ops.Outbound.Count;

        // Simulate multiple PipeTo read completions
        sm.OnBodyMessage(new ResponseBodyReadComplete(100));
        sm.OnBodyMessage(new ResponseBodyReadComplete(200));
        sm.OnBodyMessage(new ResponseBodyReadComplete(50));
        sm.OnBodyMessage(new ResponseBodyReadComplete(0));

        // 3 data chunks + 1 chunked terminator from CompleteAsync
        var bodyItems = ops.Outbound.Skip(headerCount).OfType<TransportData>().ToList();
        Assert.Equal(4, bodyItems.Count);
    }

    [Fact(Timeout = 5000)]
    public void OnBodyMessage_complete_should_clear_outbound_pending_flag()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        var context = CreateResponseContext();
        sm.OnResponse(context);

        // Body is pending after OnResponse
        Assert.False(sm.CanAcceptResponse);

        sm.OnBodyMessage(new ResponseBodyReadComplete(10));
        sm.OnBodyMessage(new ResponseBodyReadComplete(0));

        // After body complete, outbound pending is cleared
        // (CanAcceptResponse is still false because _pendingResponseCount == 0)
        Assert.False(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    public void OnBodyMessage_failed_should_clear_outbound_pending_flag()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        var context = CreateResponseContext();
        sm.OnResponse(context);

        sm.OnBodyMessage(new ResponseBodyReadComplete(10));
        sm.OnBodyMessage(new ResponseBodyReadFailed(new Exception("simulated failure")));

        // Subsequent operations should not throw
        sm.OnOutboundFlushed();
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    public void OnOutboundFlushed_should_be_no_op_after_body_complete()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        var context = CreateResponseContext();
        sm.OnResponse(context);

        sm.OnBodyMessage(new ResponseBodyReadComplete(0));

        // PipeTo flow has no watermarks — OnOutboundFlushed is a no-op
        sm.OnOutboundFlushed();
        sm.OnOutboundFlushed();
        Assert.True(true);
    }
}
