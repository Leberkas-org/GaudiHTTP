using GaudiHTTP.Tests.TestSupport;
using System.Net;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Internal;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Client;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client.StateMachine;

public sealed class Http2StateMachineReconnectSpec
{
    private static WireBuffer SerializeFrame(Http2Frame frame)
    {
        var buffer = WireBuffer.Rent(frame.SerializedSize);
        var span = buffer.FullMemory.Span;
        frame.WriteTo(ref span);
        buffer.Length = frame.SerializedSize;
        return buffer;
    }

    private static GaudiClientOptions MakeConfig(int? maxConcurrentStreams = null, int? maxReconnect = null)
    {
        var options = new GaudiClientOptions();
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Reconnect_should_discard_partial_frame_buffered_from_previous_connection()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet("/a"));

        // Deliver only a partial DATA frame: header declares 100 payload bytes, 10 arrive. The
        // decoder buffers this as a remainder awaiting the missing 90 bytes.
        var partial = new byte[9 + 10];
        partial[2] = 100;          // 24-bit length = 100
        partial[3] = 0x00;         // type = DATA
        partial[8] = 1;            // stream id = 1
        var partialBuf = WireBuffer.Rent(partial.Length);
        partial.CopyTo(partialBuf.FullMemory.Span);
        partialBuf.Length = partial.Length;
        sm.DecodeServerData(TransportData.Rent(partialBuf));

        // Connection drops and is restored: the stale remainder MUST be discarded — the new
        // connection's bytes are not a continuation of the old connection's partial frame.
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo));
        ops.Outbound.Clear();

        // A complete PING on the fresh connection must decode cleanly and be acked. Without the
        // decoder reset its 17 wire bytes are swallowed as payload of the stale partial DATA frame
        // (permanent desync) and no ack is ever emitted.
        var ping = new PingFrame(new byte[8], isAck: false);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(ping)));

        var ack = DecodeOutboundFrames(ops).OfType<PingFrame>().SingleOrDefault(p => p.IsAck);
        Assert.NotNull(ack);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4.1")]
    public async Task Connection_loss_should_fault_streaming_response_body_instead_of_truncating()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet("/big"));

        // Streaming response (no Content-Length → QueuedBodyReader) with the body still in flight.
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(MakeResponseHeaders(1))));
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(new DataFrame(1, new byte[10], endStream: false))));

        var response = Assert.Single(ops.Responses);
        var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        var chunk = new byte[64];
        Assert.Equal(10, await body.ReadAsync(chunk, TestContext.Current.CancellationToken));

        // Connection lost mid-body: the handed-out stream must FAULT promptly. Completing it as a
        // short success would silently truncate the body; leaving it pending wedges the consumer.
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            while (await body.ReadAsync(chunk, TestContext.Current.CancellationToken) > 0)
            {
            }
        });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4.1")]
    public void Protocol_error_disconnect_then_reconnect_should_replay_idempotent_inflight_requests()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        var (req, pending) = MakeTrackedGet("/a");
        sm.OnRequest(req);
        ops.Outbound.Clear();

        // Corrupt frame header: 24-bit length 0xFFFFFF far exceeds the advertised
        // SETTINGS_MAX_FRAME_SIZE → HttpProtocolException inside DecodeServerData → the SM must
        // emit DisconnectTransport instead of throwing (RFC 9113 §5.4.1 connection error).
        var garbage = new byte[9];
        garbage[0] = 0xFF;
        garbage[1] = 0xFF;
        garbage[2] = 0xFF;
        var garbageBuf = WireBuffer.Rent(garbage.Length);
        garbage.CopyTo(garbageBuf.FullMemory.Span);
        garbageBuf.Length = garbage.Length;
        sm.DecodeServerData(TransportData.Rent(garbageBuf));

        Assert.Contains(ops.Outbound, o => o is DisconnectTransport);

        // The transport answers every DisconnectTransport with a TransportDisconnected echo
        // (TcpConnectionStateMachine contract) — that echo is what starts the reconnect.
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        Assert.True(sm.IsReconnecting);
        Assert.Contains(ops.Outbound, o => o is ConnectTransport);
        ops.Outbound.Clear();

        // New lease acquired → TransportConnected → the buffered idempotent GET is replayed.
        // The first outbound item is the re-sent connection preface (magic bytes, not frames),
        // so only the last TransportData — the replayed request — is frame-decoded.
        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo));
        Assert.False(sm.IsReconnecting);
        var replayed = ops.Outbound.OfType<TransportData>().Last();
        Assert.Contains(new FrameDecoder().Decode(replayed.Buffer), f => f is HeadersFrame);
        Assert.False(pending.GetValueTask().IsFaulted);
    }

    private static HeadersFrame MakeResponseHeaders(int streamId)
    {
        var encoder = new HpackEncoder(useHuffman: false);
        var hpack = encoder.Encode([(":status", "200")]);
        return new HeadersFrame(streamId, hpack, endStream: false, endHeaders: true);
    }

    private static IReadOnlyList<Http2Frame> DecodeOutboundFrames(FakeClientOps ops)
    {
        var decoder = new FrameDecoder();
        var result = new List<Http2Frame>();
        foreach (var item in ops.Outbound)
        {
            if (item is TransportData { Buffer: var buffer })
            {
                result.AddRange(decoder.Decode(buffer));
            }
        }

        return result;
    }
}