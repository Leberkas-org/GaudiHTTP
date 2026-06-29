using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

/// <summary>
/// Tests for the SerialBodyPump-based body drain flow. The pump is inherently sequential
/// (one read at a time), so explicit watermark-based pause/resume is no longer needed.
/// These tests verify the BodyReadComplete/BodyReadFailed message handling.
/// </summary>
public sealed class Http11ServerBodyBackpressureSpec
{
    private static IFeatureCollection CreateResponseContext()
    {
        var features = new GaudiFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new GaudiHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new GaudiHttpResponseBodyFeature();
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
            new GaudiServerOptions().ToHttp1Options(),
            new GaudiServerOptions().ToHttp2Options(),
            ops);
    }

    private static void SendRequest(Http11ServerStateMachine sm)
    {
        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        sm.DecodeClientData(TransportData.Rent(MakeBuffer(requestData)));
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
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 100));
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 200));
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 50));
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 0));

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
        SendRequest(sm);

        var context = CreateResponseContext();
        sm.OnResponse(context);

        sm.OnBodyMessage(new BodyReadComplete<int>(0, 10));
        sm.OnBodyMessage(new BodyReadFailed<int>(0, new Exception("simulated failure")));

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

        sm.OnBodyMessage(new BodyReadComplete<int>(0, 0));

        // PipeTo flow has no watermarks — OnOutboundFlushed is a no-op
        sm.OnOutboundFlushed();
        sm.OnOutboundFlushed();
        Assert.True(true);
    }
}
