using System.Net;
using Servus.Akka.Transport;
using GaudiHTTP.Internal;
using GaudiHTTP.Protocol.Syntax.Http11.Client;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Client;

public sealed class Http11StateMachineReconnectSpec
{
    private static HttpRequestMessage MakeRequest(string path = "/")
        => new(HttpMethod.Get, $"http://example.com{path}")
        {
            Version = new Version(1, 1)
        };

    private static (HttpRequestMessage Request, PendingRequest Pending) MakeTrackedRequest(string path = "/")
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://example.com{path}")
        {
            Version = new Version(1, 1)
        };
        request.Options.Set(OptionsKey.Key, pending);
        request.Options.Set(OptionsKey.VersionKey, version);
        return (request, pending);
    }

    private static (HttpRequestMessage Request, PendingRequest Pending) MakeTrackedRequest(
        HttpMethod method, string path = "/")
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var request = new HttpRequestMessage(method, $"http://example.com{path}")
        {
            Version = new Version(1, 1)
        };
        request.Options.Set(OptionsKey.Key, pending);
        request.Options.Set(OptionsKey.VersionKey, version);
        return (request, pending);
    }

    private static readonly ConnectionInfo DummyConnectionInfo = new(
        new IPEndPoint(IPAddress.Loopback, 5000),
        new IPEndPoint(IPAddress.Loopback, 80),
        TransportProtocol.Tcp);

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void DecodeServerData_should_start_reconnect_on_disconnect_with_inflight_requests()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 4, http1MaxReconnectAttempts: 3), ops);
        sm.OnRequest(MakeRequest("/a"));
        sm.OnRequest(MakeRequest("/b"));
        ops.Outbound.Clear();

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.False(sm.HasInFlightRequests);
        Assert.Single(ops.Outbound, item => item is ConnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void DecodeServerData_should_set_CanAcceptRequest_false_when_reconnecting()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 4, http1MaxReconnectAttempts: 3), ops);
        sm.OnRequest(MakeRequest());

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void DecodeServerData_should_replay_buffered_requests_on_connection_restored()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 4, http1MaxReconnectAttempts: 3), ops);
        sm.OnRequest(MakeRequest("/a"));
        sm.OnRequest(MakeRequest("/b"));
        ops.Outbound.Clear();
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        ops.Outbound.Clear();

        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo));

        Assert.False(sm.IsReconnecting);
        Assert.True(sm.HasInFlightRequests);
        Assert.Equal(2, ops.Outbound.OfType<TransportData>().Count());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void DecodeServerData_should_fail_requests_when_max_reconnect_attempts_exceeded()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 4, http1MaxReconnectAttempts: 1), ops);
        var (request, pending) = MakeTrackedRequest();
        sm.OnRequest(request);

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        var task = pending.GetValueTask();
        Assert.True(task.IsFaulted);
        Assert.False(sm.IsReconnecting);
        Assert.False(sm.CanAcceptRequest);
        Assert.Contains(ops.Outbound, o => o is DisconnectTransport);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_fail_inflight_requests_instead_of_dropping_them()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 4), ops);
        var (req, pending) = MakeTrackedRequest(HttpMethod.Get, "/a");
        sm.OnRequest(req);

        // Stage teardown (KillSwitch abort → PostStop → Cleanup) must FAIL in-flight requests, not
        // silently drop them — otherwise the caller hangs until its client-side timeout.
        sm.Cleanup();

        Assert.True(pending.GetValueTask().IsFaulted);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_during_reconnect_should_fail_buffered_replay_requests()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 4, http1MaxReconnectAttempts: 3), ops);
        var (req, pending) = MakeTrackedRequest(HttpMethod.Get, "/a");
        sm.OnRequest(req);

        // Move the request into the reconnect replay buffer, then tear the stage down.
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        sm.Cleanup();

        Assert.True(pending.GetValueTask().IsFaulted);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void DecodeServerData_should_not_replay_non_idempotent_request_on_reconnect()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 4, http1MaxReconnectAttempts: 3), ops);
        var (post, postPending) = MakeTrackedRequest(HttpMethod.Post, "/submit");
        sm.OnRequest(post);
        ops.Outbound.Clear();

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        // The server may already have processed the POST; replaying it risks a duplicate.
        Assert.True(postPending.GetValueTask().IsFaulted);

        // On restore there is nothing safe to replay — no request bytes go back out.
        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo));
        Assert.Empty(ops.Outbound.OfType<TransportData>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void DecodeServerData_should_replay_idempotent_but_fail_non_idempotent_on_reconnect()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 4, http1MaxReconnectAttempts: 3), ops);
        var (get, getPending) = MakeTrackedRequest(HttpMethod.Get, "/a");
        var (post, postPending) = MakeTrackedRequest(HttpMethod.Post, "/b");
        sm.OnRequest(get);
        sm.OnRequest(post);
        ops.Outbound.Clear();
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(postPending.GetValueTask().IsFaulted);
        Assert.False(getPending.GetValueTask().IsFaulted);

        ops.Outbound.Clear();
        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo));

        // Only the idempotent GET is replayed.
        Assert.Single(ops.Outbound.OfType<TransportData>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void DecodeServerData_should_defer_retry_behind_backoff_then_connect_when_timer_fires()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 4, http1MaxReconnectAttempts: 3), ops);
        sm.OnRequest(MakeRequest());

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        var countAfterFirst = ops.Outbound.OfType<ConnectTransport>().Count();

        // Second failure schedules a backoff timer instead of reconnecting immediately.
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.Equal(countAfterFirst, ops.Outbound.OfType<ConnectTransport>().Count());
        Assert.Contains(ops.ScheduledTimers, t => t.Name == "reconnect-backoff");

        // Firing the timer performs the actual reconnect.
        sm.OnTimerFired("reconnect-backoff");
        Assert.Equal(countAfterFirst + 1, ops.Outbound.OfType<ConnectTransport>().Count());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void DecodeServerData_with_zero_backoff_should_connect_immediately_on_retry()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(
            TestClientOptions.Create(maxPipelineDepth: 4, http1MaxReconnectAttempts: 3, http1ReconnectInitialBackoff: TimeSpan.Zero), ops);
        sm.OnRequest(MakeRequest());

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        var countAfterFirst = ops.Outbound.OfType<ConnectTransport>().Count();

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.Equal(countAfterFirst + 1, ops.Outbound.OfType<ConnectTransport>().Count());
        Assert.DoesNotContain(ops.ScheduledTimers, t => t.Name == "reconnect-backoff");
    }
}