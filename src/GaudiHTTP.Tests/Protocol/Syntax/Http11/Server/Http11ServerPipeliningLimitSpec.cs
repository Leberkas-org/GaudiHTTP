using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class Http11ServerPipeliningLimitSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_accept_requests_up_to_limit()
    {
        var ops = new FakeServerOps();
        var options = new GaudiServerOptions
        {
            Http1 =
            {
                MaxPipelinedRequests = 3
            }
        };
        var sm = new Http11ServerStateMachine(options.ToHttp1Options(), options.ToHttp2Options(), ops);
        var request = BuildPipelinedRequests(3);

        sm.ConnectTransport(Encoding.ASCII.GetBytes(request), ops);

        Assert.Equal(3, ops.Requests.Count);
        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_enforce_pipelining_limit()
    {
        var ops = new FakeServerOps();
        var options = new GaudiServerOptions
        {
            Http1 =
            {
                MaxPipelinedRequests = 2
            }
        };
        var sm = new Http11ServerStateMachine(options.ToHttp1Options(), options.ToHttp2Options(), ops);
        var request = BuildPipelinedRequests(4); // Try to send 4 requests

        sm.ConnectTransport(Encoding.ASCII.GetBytes(request), ops);

        // Should only accept 2 requests (the limit)
        Assert.Equal(2, ops.Requests.Count);
        // Should mark connection for closure due to limit
        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_close_after_limit_reached_response()
    {
        var ops = new FakeServerOps();
        var options = new GaudiServerOptions
        {
            Http1 =
            {
                MaxPipelinedRequests = 1
            }
        };
        var sm = new Http11ServerStateMachine(options.ToHttp1Options(), options.ToHttp2Options(), ops);
        var request = BuildPipelinedRequests(2); // Try to send 2 requests with limit 1
        var transport = sm.ConnectTransport(Encoding.ASCII.GetBytes(request), ops);

        Assert.Single(ops.Requests);
        Assert.True(sm.ShouldComplete);

        // Send response - should trigger connection close
        var context = ServerTestContext.CreateResponse();
        sm.OnResponse(context);

        // Verify the response was sent
        Assert.True(transport.WrittenCount > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_default_limit_should_be_16()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);
        var request = BuildPipelinedRequests(16);

        sm.ConnectTransport(Encoding.ASCII.GetBytes(request), ops);

        Assert.Equal(16, ops.Requests.Count);
        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_reject_17th_request_with_default_limit()
    {
        var ops = new FakeServerOps();
        var sm = new Http11ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);
        var request = BuildPipelinedRequests(17);

        sm.ConnectTransport(Encoding.ASCII.GetBytes(request), ops);

        Assert.Equal(16, ops.Requests.Count);
        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_accept_high_limit()
    {
        var ops = new FakeServerOps();
        var options = new GaudiServerOptions
        {
            Http1 =
            {
                MaxPipelinedRequests = 100
            }
        };
        var sm = new Http11ServerStateMachine(options.ToHttp1Options(), options.ToHttp2Options(), ops);
        var request = BuildPipelinedRequests(100);

        sm.ConnectTransport(Encoding.ASCII.GetBytes(request), ops);

        Assert.Equal(100, ops.Requests.Count);
        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_should_throw_on_invalid_limit()
    {
        var ops = new FakeServerOps();

        var invalidOpts1 = new GaudiServerOptions
        {
            Http1 =
            {
                MaxPipelinedRequests = 0
            }
        };
        Assert.Throws<ArgumentException>(() => new Http11ServerStateMachine(invalidOpts1.ToHttp1Options(), invalidOpts1.ToHttp2Options(), ops));

        var invalidOpts2 = new GaudiServerOptions
        {
            Http1 =
            {
                MaxPipelinedRequests = -1
            }
        };
        Assert.Throws<ArgumentException>(() => new Http11ServerStateMachine(invalidOpts2.ToHttp1Options(), invalidOpts2.ToHttp2Options(), ops));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.4")]
    public void ServerStateMachine_limit_applies_per_buffer()
    {
        var ops = new FakeServerOps();
        var options = new GaudiServerOptions
        {
            Http1 =
            {
                MaxPipelinedRequests = 2
            }
        };
        var sm = new Http11ServerStateMachine(options.ToHttp1Options(), options.ToHttp2Options(), ops);

        var transport = sm.ConnectTransport(Encoding.ASCII.GetBytes(BuildPipelinedRequests(2)), ops);
        Assert.Equal(2, ops.Requests.Count);

        transport.FeedMore(sm, ops, Encoding.ASCII.GetBytes(BuildPipelinedRequests(2)));

        Assert.True(sm.ShouldComplete);
    }

    private static string BuildPipelinedRequests(int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            sb.Append($"GET /page{i} HTTP/1.1\r\n");
            sb.Append("Host: example.com\r\n");
            sb.Append("Content-Length: 0\r\n");
            sb.Append("\r\n");
        }

        return sb.ToString();
    }
}

