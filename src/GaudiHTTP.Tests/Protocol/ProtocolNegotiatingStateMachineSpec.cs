using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol;

public sealed class ProtocolNegotiatingStateMachineSpec
{

    private static TransportConnected MakeConnected(SslApplicationProtocol? alpn = null)
    {
        var security = alpn is not null
            ? new SecurityInfo(SslProtocols.Tls13, alpn.Value)
            : null;

        var info = new ConnectionInfo(
            new IPEndPoint(IPAddress.Loopback, 443),
            new IPEndPoint(IPAddress.Loopback, 50000),
            alpn is not null ? TransportProtocol.Tls : TransportProtocol.Tcp,
            security);

        return new TransportConnected(info);
    }

    private static TransportData MakeData(byte[] data)
    {
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return TransportData.Rent(buffer);
    }

    // Task 2: ALPN Detection Tests

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http2_for_alpn_h2()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        sm.DecodeClientData(MakeConnected(SslApplicationProtocol.Http2));

        Assert.True(sm.CanAcceptResponse || !sm.ShouldComplete);
        Assert.True(ops.ScheduledTimers.Any(t => t.Name == "keep-alive-timeout"),
            "keep-alive-timeout should be scheduled");
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http11_for_alpn_http11()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        sm.DecodeClientData(MakeConnected(SslApplicationProtocol.Http11));

        Assert.False(sm.CanAcceptResponse);
        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http11_for_default_alpn()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        sm.DecodeClientData(MakeConnected(default(SslApplicationProtocol)));

        Assert.False(sm.CanAcceptResponse);
        Assert.False(sm.ShouldComplete);
    }

    // Task 3: Preface Sniffing Tests

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http2_for_pri_preface()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());

        var preface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        sm.DecodeClientData(MakeData(preface));

        Assert.True(ops.ScheduledTimers.Any(t => t.Name == "keep-alive-timeout"),
            "keep-alive-timeout should be scheduled");
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http11_for_get_request()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());

        var request = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        sm.DecodeClientData(MakeData(request));

        Assert.Single(ops.Requests);
        var ctx = ops.Requests[0];
        var feature = ctx.Get<IHttpRequestFeature>();
        Assert.NotNull(feature);
        Assert.Equal("GET", feature.Method);
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http11_for_post_request()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());

        var request = "POST / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"u8.ToArray();
        sm.DecodeClientData(MakeData(request));

        Assert.Single(ops.Requests);
        var ctx = ops.Requests[0];
        var feature = ctx.Get<IHttpRequestFeature>();
        Assert.NotNull(feature);
        Assert.Equal("POST", feature.Method);
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_stay_sniffing_for_insufficient_data()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());
        sm.DecodeClientData(MakeData("PR"u8.ToArray()));

        Assert.False(sm.CanAcceptResponse);
        Assert.False(sm.ShouldComplete);
        Assert.Empty(ops.Requests);
        // The negotiation idle-timeout is armed while sniffing.
        Assert.Single(ops.ScheduledTimers);
    }

    [Fact(Timeout = 5000)]
    public void Sniffing_should_abort_when_buffered_bytes_exceed_cap()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());

        var garbage = new byte[128 * 1024];
        Array.Fill(garbage, (byte)'A');
        sm.DecodeClientData(MakeData(garbage));

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void Sniffing_should_identify_http2_when_first_segment_exceeds_sniff_cap()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());

        // A large first TCP segment: the HTTP/2 preface coalesced with SETTINGS + request frames,
        // exceeding the 64 KiB sniff cap (common for concurrent / large HTTP/2). The preface MUST be
        // recognized (HTTP/2 activated, keep-alive scheduled) rather than aborted by the cap before
        // identification — the regression that broke concurrent HTTP/2 large-payload round-trips.
        var preface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        var segment = new byte[96 * 1024];
        preface.CopyTo(segment.AsSpan());
        sm.DecodeClientData(MakeData(segment));

        Assert.True(ops.ScheduledTimers.Any(t => t.Name == "keep-alive-timeout"),
            "HTTP/2 should have been activated despite the oversized first segment, not aborted by the sniff cap.");
    }

    [Fact(Timeout = 5000)]
    public void Sniffing_should_arm_idle_timeout_and_abort_when_it_fires()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());

        var timer = Assert.Single(ops.ScheduledTimers);
        Assert.False(sm.ShouldComplete);

        sm.OnTimerFired(timer.Name);

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3.2")]
    public void MaxConcurrentRequests_should_serialize_dispatch_for_negotiated_http11()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());
        sm.DecodeClientData(MakeData("GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"u8.ToArray()));

        // HTTP/1.1 responses are positional on the wire, so the negotiator must forward the inner
        // machine's one-at-a-time dispatch limit — otherwise the shared, completion-ordered bridge
        // can reorder pipelined responses (RFC 9112 §9.3.2).
        Assert.Equal(1, sm.MaxConcurrentRequests);
    }

    [Fact(Timeout = 5000)]
    public void MaxConcurrentRequests_should_stay_unbounded_for_negotiated_http2()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        sm.DecodeClientData(MakeConnected(SslApplicationProtocol.Http2));

        // HTTP/2 routes responses to streams by id, so concurrent dispatch must remain unbounded.
        Assert.Equal(int.MaxValue, sm.MaxConcurrentRequests);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_dispose_buffered_data()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        sm.DecodeClientData(MakeConnected());
        sm.Cleanup();

        Assert.False(sm.ShouldComplete);
    }
}

