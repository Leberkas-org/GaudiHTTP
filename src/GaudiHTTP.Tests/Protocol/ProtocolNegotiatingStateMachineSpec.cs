using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.TestKit;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol;

public sealed class ProtocolNegotiatingStateMachineSpec
{

    private static TransportConnected MakeConnected(SslApplicationProtocol? alpn = null,
        TestPipeTransport? transport = null)
    {
        var security = alpn is not null
            ? new SecurityInfo(SslProtocols.Tls13, alpn.Value)
            : null;

        var info = new ConnectionInfo(
            new IPEndPoint(IPAddress.Loopback, 443),
            new IPEndPoint(IPAddress.Loopback, 50000),
            alpn is not null ? TransportProtocol.Tls : TransportProtocol.Tcp,
            security);

        return new TransportConnected(info, transport);
    }

    private static TransportConnected MakeConnected(TestPipeTransport transport)
        => new(new ConnectionInfo(
            new IPEndPoint(IPAddress.Loopback, 443),
            new IPEndPoint(IPAddress.Loopback, 50000),
            TransportProtocol.Tcp),
            transport);

    /// <summary>
    /// Feeds data to the transport's input pipe without awaiting <see cref="PipeWriter.FlushAsync"/>.
    /// The default <see cref="Pipe"/> pauses the writer when unflushed bytes exceed 64 KB, which would
    /// deadlock if no reader is consuming yet. The data is committed (visible to a subsequent
    /// <see cref="PipeReader.ReadAsync"/>) even though the flush task is not awaited.
    /// </summary>
    private static void FeedInputDirect(TestPipeTransport transport, byte[] data)
    {
        var writer = transport.InputWriter;
        var mem = writer.GetMemory(data.Length);
        data.CopyTo(mem);
        writer.Advance(data.Length);
        _ = writer.FlushAsync();
    }

    // Task 2: ALPN Detection Tests

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_select_http2_for_alpn_h2()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        var transport = new TestPipeTransport();
        sm.DecodeClientData(MakeConnected(SslApplicationProtocol.Http2, transport));

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
    public async Task DecodeClientData_should_select_http2_for_pri_preface()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        var transport = new TestPipeTransport();
        await transport.FeedInputAsync("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray());

        sm.DecodeClientData(MakeConnected(transport));

        Assert.True(ops.ScheduledTimers.Any(t => t.Name == "keep-alive-timeout"),
            "keep-alive-timeout should be scheduled");
    }

    [Fact(Timeout = 5000)]
    public async Task DecodeClientData_should_select_http11_for_get_request()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        var transport = new TestPipeTransport();
        await transport.FeedInputAsync("GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"u8.ToArray());

        sm.DecodeClientData(MakeConnected(transport));

        Assert.Single(ops.Requests);
        var ctx = ops.Requests[0];
        var feature = ctx.Get<IHttpRequestFeature>();
        Assert.NotNull(feature);
        Assert.Equal("GET", feature.Method);
    }

    [Fact(Timeout = 5000)]
    public async Task DecodeClientData_should_select_http11_for_post_request()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        var transport = new TestPipeTransport();
        await transport.FeedInputAsync("POST / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"u8.ToArray());

        sm.DecodeClientData(MakeConnected(transport));

        Assert.Single(ops.Requests);
        var ctx = ops.Requests[0];
        var feature = ctx.Get<IHttpRequestFeature>();
        Assert.NotNull(feature);
        Assert.Equal("POST", feature.Method);
    }

    [Fact(Timeout = 5000)]
    public async Task DecodeClientData_should_stay_sniffing_for_insufficient_data()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        var transport = new TestPipeTransport();
        await transport.FeedInputAsync("PR"u8.ToArray());

        sm.DecodeClientData(MakeConnected(transport));

        Assert.False(sm.CanAcceptResponse);
        Assert.False(sm.ShouldComplete);
        Assert.Empty(ops.Requests);
        Assert.Single(ops.ScheduledTimers);
    }

    [Fact(Timeout = 5000)]
    public void Sniffing_should_abort_when_buffered_bytes_exceed_cap()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        var transport = new TestPipeTransport();
        var garbage = new byte[128 * 1024];
        Array.Fill(garbage, (byte)'A');
        FeedInputDirect(transport, garbage);

        sm.DecodeClientData(MakeConnected(transport));

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public async Task Cleartext_h2_preface_should_be_rejected_when_http2_not_allowed()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops, HttpProtocols.Http1);

        var transport = new TestPipeTransport();
        await transport.FeedInputAsync("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray());

        sm.DecodeClientData(MakeConnected(transport));

        Assert.True(sm.ShouldComplete, "h2c prior-knowledge must be rejected on an Http1-only endpoint");
        Assert.DoesNotContain(ops.ScheduledTimers, t => t.Name == "keep-alive-timeout");
    }

    [Fact(Timeout = 5000)]
    public async Task Cleartext_h2_preface_should_be_accepted_when_http2_allowed()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops, HttpProtocols.Http1AndHttp2);

        var transport = new TestPipeTransport();
        await transport.FeedInputAsync("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray());

        sm.DecodeClientData(MakeConnected(transport));

        Assert.False(sm.ShouldComplete);
        Assert.Contains(ops.ScheduledTimers, t => t.Name == "keep-alive-timeout");
    }

    [Fact(Timeout = 5000)]
    public void Sniffing_should_identify_http2_when_first_segment_exceeds_sniff_cap()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        var transport = new TestPipeTransport();
        var preface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
        var segment = new byte[96 * 1024];
        preface.CopyTo(segment.AsSpan());
        FeedInputDirect(transport, segment);

        sm.DecodeClientData(MakeConnected(transport));

        Assert.True(ops.ScheduledTimers.Any(t => t.Name == "keep-alive-timeout"),
            "HTTP/2 should have been activated despite the oversized first segment, not aborted by the sniff cap.");
    }

    [Fact(Timeout = 5000)]
    public void Sniffing_should_arm_idle_timeout_and_abort_when_it_fires()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        var transport = new TestPipeTransport();
        sm.DecodeClientData(MakeConnected(transport));

        var timer = Assert.Single(ops.ScheduledTimers);
        Assert.False(sm.ShouldComplete);

        sm.OnTimerFired(timer.Name);

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3.2")]
    public async Task MaxConcurrentRequests_should_serialize_dispatch_for_negotiated_http11()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        var transport = new TestPipeTransport();
        await transport.FeedInputAsync("GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n"u8.ToArray());

        sm.DecodeClientData(MakeConnected(transport));

        Assert.Equal(1, sm.MaxConcurrentRequests);
    }

    [Fact(Timeout = 5000)]
    public void MaxConcurrentRequests_should_stay_unbounded_for_negotiated_http2()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        var transport = new TestPipeTransport();
        sm.DecodeClientData(MakeConnected(SslApplicationProtocol.Http2, transport));

        // HTTP/2 routes responses to streams by id, so concurrent dispatch must remain unbounded.
        Assert.Equal(int.MaxValue, sm.MaxConcurrentRequests);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_dispose_buffered_data()
    {
        var ops = new FakeServerOps();
        var sm = new ProtocolNegotiatingStateMachine(new GaudiServerOptions(), ops);

        var transport = new TestPipeTransport();
        sm.DecodeClientData(MakeConnected(transport));
        sm.Cleanup();

        Assert.False(sm.ShouldComplete);
    }
}

