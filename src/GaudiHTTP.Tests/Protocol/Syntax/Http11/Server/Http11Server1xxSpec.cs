using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11Server1xxSpec
{
    private static Http11ServerStateMachine CreateSm(FakeServerOps ops)
    {
        var options = new GaudiServerOptions();
        return new Http11ServerStateMachine(options.ToHttp1Options(), options.ToHttp2Options(), ops);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void SendInformational_should_emit_1xx_status_line_and_headers()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var transport = sm.ConnectTransport(
            Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n"),
            ops
        );

        Assert.Single(ops.Requests);
        var features = ops.Requests[0];
        var informational = features.Get<GaudiInformationalResponseFeature>();
        Assert.NotNull(informational);

        informational.SendInformational(103, new HeaderDictionary
        {
            ["Link"] = "</style.css>; rel=preload"
        });

        var wire = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.Contains("HTTP/1.1 103", wire);
        Assert.Contains("Link: </style.css>; rel=preload", wire);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void SendInformational_should_not_decrement_pending_response_count()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.ConnectTransport(
            Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n"),
            ops
        );

        var features = ops.Requests[0];
        features.Get<GaudiInformationalResponseFeature>()!.SendInformational(100, new HeaderDictionary());

        Assert.True(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void Final_response_should_follow_1xx_on_same_connection()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var transport = sm.ConnectTransport(
            Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n"),
            ops
        );

        var features = ops.Requests[0];
        features.Get<GaudiInformationalResponseFeature>()!.SendInformational(100, new HeaderDictionary());

        var responseFeature = features.Get<IHttpResponseFeature>()!;
        responseFeature.StatusCode = 200;
        sm.OnResponse(features);

        var wire = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.Contains("HTTP/1.1 100", wire);
        Assert.Contains("HTTP/1.1 200", wire);
        Assert.True(wire.IndexOf("100") < wire.IndexOf("200"));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void SendInformational_should_not_recycle_features()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.ConnectTransport(
            Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n"),
            ops
        );

        var features = ops.Requests[0];
        features.Get<GaudiInformationalResponseFeature>()!.SendInformational(100, new HeaderDictionary());

        Assert.Empty(ops.ResponseBodyCompletions);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void SendInformational_should_not_schedule_keepalive_timer()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.ConnectTransport(
            Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n"),
            ops
        );

        var features = ops.Requests[0];
        features.Get<GaudiInformationalResponseFeature>()!.SendInformational(100, new HeaderDictionary());

        Assert.DoesNotContain(ops.ScheduledTimers, t => t.Name.Contains("keep-alive"));
    }
}
