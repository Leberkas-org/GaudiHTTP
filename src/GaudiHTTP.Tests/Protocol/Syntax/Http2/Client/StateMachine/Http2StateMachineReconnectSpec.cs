using System.Net;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Internal;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Client;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client.StateMachine;

public sealed class Http2StateMachineReconnectSpec
{
    private static TransportBuffer SerializeFrame(Http2Frame frame)
    {
        var buffer = TransportBuffer.Rent(frame.SerializedSize);
        var span = buffer.FullMemory.Span;
        frame.WriteTo(ref span);
        buffer.Length = frame.SerializedSize;
        return buffer;
    }

    private static TurboClientOptions MakeConfig(int? maxConcurrentStreams = null, int? maxReconnect = null)
    {
        var options = new TurboClientOptions();
        if (maxConcurrentStreams.HasValue) options.Http2.MaxConcurrentStreams = maxConcurrentStreams.Value;
        if (maxReconnect.HasValue) options.Http2.MaxReconnectAttempts = maxReconnect.Value;
        return options;
    }

    private static HttpRequestMessage MakeGet(string path = "/") =>
        new(HttpMethod.Get, $"https://example.com{path}");

    private static HttpRequestMessage MakePost(string path = "/") =>
        new(HttpMethod.Post, $"https://example.com{path}");

    private static (HttpRequestMessage Request, PendingRequest Pending) MakeTrackedGet(string path = "/")
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var req = new HttpRequestMessage(HttpMethod.Get, $"https://example.com{path}");
        req.Options.Set(OptionsKey.Key, pending);
        req.Options.Set(OptionsKey.VersionKey, version);
        return (req, pending);
    }

    private static (HttpRequestMessage Request, PendingRequest Pending) MakeTrackedPost(string path = "/")
    {
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var req = new HttpRequestMessage(HttpMethod.Post, $"https://example.com{path}");
        req.Options.Set(OptionsKey.Key, pending);
        req.Options.Set(OptionsKey.VersionKey, version);
        return (req, pending);
    }

    private static readonly ConnectionInfo DummyConnectionInfo = new(
        new IPEndPoint(IPAddress.Loopback, 5000),
        new IPEndPoint(IPAddress.Loopback, 443),
        TransportProtocol.Tcp);

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeServerData_should_start_reconnect_on_disconnect_with_inflight()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet("/a"));
        sm.OnRequest(MakeGet("/b"));
        ops.Outbound.Clear();

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.Equal(2, sm.ReconnectBufferCount);
        Assert.Single(ops.Outbound, item => item is ConnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeServerData_should_not_replay_non_idempotent_requests()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet("/a")); // stream 1
        sm.OnRequest(MakePost("/b")); // stream 3
        ops.Outbound.Clear();

        // A non-graceful (error) GOAWAY forces a reconnect; the idempotent GET is replayed but the
        // non-idempotent POST must NOT be (the server may have partially processed it).
        var goaway = new GoAwayFrame(3, Http2ErrorCode.InternalError);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(goaway)));

        Assert.True(sm.IsReconnecting);
        Assert.Equal(1, sm.ReconnectBufferCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeServerData_should_replay_requests_on_connection_restored()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet("/a"));
        ops.Outbound.Clear();

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        ops.Outbound.Clear();

        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo));

        Assert.False(sm.IsReconnecting);
        Assert.NotEmpty(ops.Outbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeServerData_should_set_CanAcceptRequest_false_when_reconnecting()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeServerData_should_fail_when_max_reconnect_exceeded()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(maxReconnect: 1), ops);
        sm.PreStart();
        var (req, pending) = MakeTrackedGet();
        sm.OnRequest(req);

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        var task = pending.GetValueTask();
        Assert.True(task.IsFaulted, "Request should be faulted after max reconnect attempts");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeServerData_should_emit_new_connect_when_reconnect_under_limit()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(maxReconnect: 3), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        var countAfterFirst = ops.Outbound.OfType<ConnectTransport>().Count();

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.IsReconnecting);
        Assert.Equal(countAfterFirst + 1, ops.Outbound.OfType<ConnectTransport>().Count());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Graceful_goaway_should_drain_inflight_streams_at_or_below_last_id_without_reconnecting()
    {
        // RFC 9113 §6.8: "Activity on streams numbered lower than or equal to the last stream
        // identifier might still complete successfully ... maintaining the connection in an 'open'
        // state until all in-progress streams complete." A graceful (NO_ERROR) GOAWAY whose
        // LastStreamId covers all in-flight streams must NOT trigger a reconnect, and must NOT drop
        // the in-flight non-idempotent POST — the server has committed to finish it.
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet("/a"));               // stream 1
        var (post, postPending) = MakeTrackedPost("/b"); // stream 3
        sm.OnRequest(post);
        ops.Outbound.Clear();

        var goaway = new GoAwayFrame(3, Http2ErrorCode.NoError);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(goaway)));

        Assert.False(sm.IsReconnecting);                 // no eager reconnect
        Assert.False(sm.CanAcceptRequest);               // ...but no NEW streams either
        Assert.DoesNotContain(ops.Outbound, o => o is ConnectTransport);
        Assert.False(postPending.GetValueTask().IsCompleted, // the POST is NOT dropped — still draining
            "graceful GOAWAY must not drop an in-flight stream <= LastStreamId");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Graceful_goaway_then_close_should_replay_streams_above_last_id_even_when_non_idempotent()
    {
        // The race case, handled by deferred reconnect: a POST on a stream the server discarded
        // (id > LastStreamId) must be replayable — the server provably never processed it. The graceful
        // GOAWAY first lets the connection drain (no eager reconnect); only when the server CLOSES the
        // connection do we reconnect, classifying against the remembered LastStreamId so the
        // > LastStreamId POST is replayed while a <= LastStreamId POST that never completed is not.
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        var (postLow, postLowPending) = MakeTrackedPost("/a"); // stream 1 (<= LastStreamId)
        sm.OnRequest(postLow);
        sm.OnRequest(MakePost("/b"));                          // stream 3 (> LastStreamId)
        ops.Outbound.Clear();

        // Phase 1 — graceful GOAWAY(LastStreamId=1): drain, no reconnect, nothing dropped yet.
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(new GoAwayFrame(1, Http2ErrorCode.NoError))));
        Assert.False(sm.IsReconnecting);
        Assert.DoesNotContain(ops.Outbound, o => o is ConnectTransport);
        Assert.False(postLowPending.GetValueTask().IsCompleted);

        // Phase 2 — server closes the drained connection: reconnect + replay. Stream 3 (> 1) is replayed
        // even though it's a POST; stream 1 (<= 1, non-idempotent, never completed) is dropped.
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Graceful));
        Assert.True(sm.IsReconnecting);
        Assert.Equal(1, sm.ReconnectBufferCount); // only stream 3 buffered for replay
        Assert.Contains(ops.Outbound, o => o is ConnectTransport);
        Assert.True(postLowPending.GetValueTask().IsFaulted); // stream 1 dropped (may have been processed)
    }
}