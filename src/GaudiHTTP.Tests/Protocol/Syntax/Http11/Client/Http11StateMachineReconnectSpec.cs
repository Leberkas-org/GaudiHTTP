using System.Net;
using System.Text;
using Servus.Akka.Transport;
using GaudiHTTP.Internal;
using GaudiHTTP.Protocol.Syntax.Http11.Client;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Client;

public sealed class Http11StateMachineReconnectSpec
{
    private static void DrainBodyMessages(Http11ClientStateMachine sm, FakeClientOps ops)
    {
        while (ops.BodyMessages.Count > 0)
        {
            var msg = ops.BodyMessages[0];
            ops.BodyMessages.RemoveAt(0);
            sm.OnBodyMessage(msg);
        }
    }

    /// <summary>Forward-only body stream: CanSeek == false, cannot be rewound for replay.</summary>
    private sealed class NonSeekableStream(int length) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(count, length - _position);
            buffer.AsSpan(offset, n).Fill(0x42);
            _position += n;
            return n;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = Math.Min(buffer.Length, length - _position);
            buffer.Span[..n].Fill(0x42);
            _position += n;
            return ValueTask.FromResult(n);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

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

        var transport = sm.ConnectTransport(ops: ops);

        Assert.False(sm.IsReconnecting);
        Assert.True(sm.HasInFlightRequests);
        var written = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.Contains("GET /a", written);
        Assert.Contains("GET /b", written);
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
        var transport = sm.ConnectTransport(ops: ops);
        Assert.Equal(0, transport.WrittenCount);
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
        var transport = sm.ConnectTransport(ops: ops);

        // Only the idempotent GET is replayed.
        var written = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.Contains("GET /a", written);
        Assert.DoesNotContain("POST", written);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void Reconnect_replay_should_resend_full_seekable_body()
    {
        const int bodySize = 64 * 1024;
        var payload = new byte[bodySize];
        new Random(1).NextBytes(payload);

        var ops = new FakeClientOps();
        var options = TestClientOptions.Create(maxPipelineDepth: 4, http1MaxReconnectAttempts: 3,
            http1ReconnectInitialBackoff: TimeSpan.Zero);
        options.RequestBodyChunkSize = 16 * 1024;
        var sm = new Http11ClientStateMachine(options, ops);
        sm.PreStart();

        var content = new ByteArrayContent(payload);
        content.Headers.ContentLength = bodySize;
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/upload")
        {
            Version = new Version(1, 1),
            Content = content,
        };

        // First attempt: the 64 KB body fits within the 256 KB pump budget, so it fully drains.
        var transport1 = sm.ConnectTransport(ops: ops);
        sm.OnRequest(request);
        DrainBodyMessages(sm, ops);
        // Headers + body written to transport; extract body portion after \r\n\r\n.
        var firstWritten = transport1.TakeWrittenBytes();
        var firstHeaderEnd = FindHeaderEnd(firstWritten);
        Assert.Equal(bodySize, firstWritten.Length - firstHeaderEnd);

        // Ungraceful disconnect: the idempotent PUT is buffered for reconnect replay.
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        Assert.True(sm.IsReconnecting);

        // Reconnect replay MUST re-send the FULL body. Before the fix the cached, already-consumed
        // content stream (Position == Length) yields 0 body bytes while headers still declare the
        // full Content-Length — the server then blocks forever on ReadAsync.
        var transport2 = sm.ConnectTransport(ops: ops);
        DrainBodyMessages(sm, ops);

        var replayedWritten = transport2.TakeWrittenBytes();
        var replayedHeaderEnd = FindHeaderEnd(replayedWritten);
        var replayedBody = replayedWritten.Length - replayedHeaderEnd;
        Assert.Equal(bodySize, replayedBody);
    }

    private static int FindHeaderEnd(byte[] data)
    {
        var text = Encoding.ASCII.GetString(data);
        var idx = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        return idx >= 0 ? idx + 4 : data.Length;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void Reconnect_replay_should_fail_fast_when_body_is_non_rewindable()
    {
        const int bodySize = 64 * 1024;

        var ops = new FakeClientOps();
        var options = TestClientOptions.Create(maxPipelineDepth: 4, http1MaxReconnectAttempts: 3,
            http1ReconnectInitialBackoff: TimeSpan.Zero);
        options.RequestBodyChunkSize = 16 * 1024;
        var sm = new Http11ClientStateMachine(options, ops);
        sm.PreStart();

        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var content = new StreamContent(new NonSeekableStream(bodySize));
        content.Headers.ContentLength = bodySize;
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/upload")
        {
            Version = new Version(1, 1),
            Content = content,
        };
        request.Options.Set(OptionsKey.Key, pending);
        request.Options.Set(OptionsKey.VersionKey, version);

        var transport1 = sm.ConnectTransport(ops: ops);
        sm.OnRequest(request);
        DrainBodyMessages(sm, ops);

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        Assert.True(sm.IsReconnecting);

        // A consumed forward-only body cannot be rewound. Replaying it would advertise the full
        // Content-Length but send a truncated body, hanging the server. Fail fast instead.
        var transport2 = sm.ConnectTransport(ops: ops);
        DrainBodyMessages(sm, ops);

        Assert.True(pending.GetValueTask().IsFaulted);
        Assert.Equal(0, transport2.WrittenCount);
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