using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11Server1xxSpec
{
    private static Http11ServerStateMachine CreateSm(FakeServerOps ops)
    {
        var options = new TurboServerOptions();
        return new Http11ServerStateMachine(options.ToHttp1Options(), options.ToHttp2Options(), ops);
    }

    private static TransportData Make(string raw)
    {
        var data = Encoding.ASCII.GetBytes(raw);
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return TransportData.Rent(buffer);
    }

    private static string Outbound(FakeServerOps ops)
    {
        var sb = new StringBuilder();
        foreach (var item in ops.Outbound)
        {
            if (item is TransportData td)
            {
                sb.Append(Encoding.ASCII.GetString(td.Buffer.Span));
            }
        }

        return sb.ToString();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void SendInformational_should_emit_1xx_status_line_and_headers()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.DecodeClientData(Make("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n"));

        Assert.Single(ops.Requests);
        var features = ops.Requests[0];
        var informational = features.Get<TurboInformationalResponseFeature>();
        Assert.NotNull(informational);

        informational.SendInformational(103, new HeaderDictionary
        {
            ["Link"] = "</style.css>; rel=preload"
        });

        var wire = Outbound(ops);
        Assert.Contains("HTTP/1.1 103", wire);
        Assert.Contains("Link: </style.css>; rel=preload", wire);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void SendInformational_should_not_decrement_pending_response_count()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.DecodeClientData(Make("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n"));

        var features = ops.Requests[0];
        features.Get<TurboInformationalResponseFeature>()!.SendInformational(100, new HeaderDictionary());

        Assert.True(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void Final_response_should_follow_1xx_on_same_connection()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.DecodeClientData(Make("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n"));

        var features = ops.Requests[0];
        features.Get<TurboInformationalResponseFeature>()!.SendInformational(100, new HeaderDictionary());

        var responseFeature = features.Get<IHttpResponseFeature>()!;
        responseFeature.StatusCode = 200;
        sm.OnResponse(features);

        var wire = Outbound(ops);
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
        sm.DecodeClientData(Make("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n"));

        var features = ops.Requests[0];
        features.Get<TurboInformationalResponseFeature>()!.SendInformational(100, new HeaderDictionary());

        Assert.Empty(ops.ResponseBodyCompletions);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.2")]
    public void SendInformational_should_not_schedule_keepalive_timer()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.DecodeClientData(Make("GET / HTTP/1.1\r\nHost: example.com\r\n\r\n"));

        var features = ops.Requests[0];
        features.Get<TurboInformationalResponseFeature>()!.SendInformational(100, new HeaderDictionary());

        Assert.DoesNotContain(ops.ScheduledTimers, t => t.Name.Contains("keep-alive"));
    }
}
