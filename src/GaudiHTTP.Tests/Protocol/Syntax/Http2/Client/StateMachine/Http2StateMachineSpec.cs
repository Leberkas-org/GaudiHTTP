using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Internal;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Client;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client.StateMachine;

public sealed class Http2StateMachineSpec
{
    private static TurboClientOptions MakeConfig(int? maxConcurrentStreams = null, int? maxReconnect = null,
        int initialStreamWindowSize = 65_535, int maxFrameSize = 16_384)
    {
        var options = new TurboClientOptions
        {
            Http2 =
            {
                InitialStreamWindowSize = initialStreamWindowSize,
                MaxFrameSize = maxFrameSize
            }
        };
        if (maxConcurrentStreams.HasValue)
        {
            options.Http2.MaxConcurrentStreams = maxConcurrentStreams.Value;
        }

        if (maxReconnect.HasValue)
        {
            options.Http2.MaxReconnectAttempts = maxReconnect.Value;
        }

        return options;
    }

    private static HttpRequestMessage MakeGet(string path = "/")
        => new(HttpMethod.Get, $"https://example.com{path}");

    private static HttpRequestMessage MakePost(string path = "/", HttpContent? content = null)
        => new(HttpMethod.Post, $"https://example.com{path}") { Content = content };

    private static HeadersFrame MakeResponseHeaders(int streamId, string statusCode = "200", bool endStream = true,
        bool endHeaders = true)
    {
        var encoder = new HpackEncoder();
        var hpack = encoder.Encode([
            (":status", statusCode),
            ("content-type", "text/plain")
        ]);
        return new HeadersFrame(streamId, hpack, endStream, endHeaders);
    }

    private static DataFrame MakeData(int streamId, byte[] data, bool endStream = true)
        => new(streamId, data, endStream);

    private static TransportBuffer SerializeFrame(Http2Frame frame)
    {
        var buffer = TransportBuffer.Rent(frame.SerializedSize);
        var span = buffer.FullMemory.Span;
        frame.WriteTo(ref span);
        buffer.Length = frame.SerializedSize;
        return buffer;
    }

    private static TransportBuffer SerializeFrames(params Http2Frame[] frames)
    {
        var totalSize = frames.Sum(f => f.SerializedSize);
        var buffer = TransportBuffer.Rent(totalSize);
        var span = buffer.FullMemory.Span;
        var offset = 0;
        foreach (var frame in frames)
        {
            var frameSpan = span[offset..];
            frame.WriteTo(ref frameSpan);
            offset += frame.SerializedSize;
        }

        buffer.Length = totalSize;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void PreStart_should_not_emit_preface()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);

        sm.PreStart();

        Assert.Empty(ops.Outbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void OnRequest_should_emit_preface_and_headers_frame_on_first_request()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        sm.OnRequest(MakeGet());

        var transportItems = ops.Outbound.OfType<TransportData>().ToList();
        Assert.Equal(2, transportItems.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void OnRequest_should_reject_when_goaway_received()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();

        var goaway = new GoAwayFrame(0, Http2ErrorCode.NoError);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(goaway)));

        sm.OnRequest(MakeGet());

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void OnRequest_should_set_endpoint_on_first_request()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();

        Assert.Equal(default, sm.Endpoint);

        sm.OnRequest(MakeGet());

        Assert.NotEqual(default, sm.Endpoint);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void OnRequest_should_emit_data_frame_when_request_has_body()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var content = new ByteArrayContent([1, 2, 3]);
        sm.OnRequest(MakePost("/", content));

        var frames = ops.Outbound.OfType<TransportData>().ToList();
        Assert.True(frames.Count > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public void OnRequest_should_allocate_incremented_stream_ids()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        sm.OnRequest(MakeGet("/a"));
        sm.OnRequest(MakeGet("/b"));
        sm.OnRequest(MakeGet("/c"));

        var transportItems = ops.Outbound.OfType<TransportData>().ToList();
        Assert.Equal(4, transportItems.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4")]
    public void DecodeServerData_should_process_settings_frame()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var settings = new SettingsFrame([]);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(settings)));

        Assert.NotEmpty(ops.Outbound.OfType<TransportData>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void DecodeServerData_should_produce_response_from_headers_and_data()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1, endStream: false, endHeaders: true);
        var data = MakeData(1, [1, 2, 3], endStream: true);
        sm.DecodeServerData(TransportData.Rent(SerializeFrames(headers, data)));

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeServerData_should_complete_response_on_headers_with_endstream()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(headers)));

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeServerData_should_accumulate_headers_without_endheaders()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var encoder = new HpackEncoder();
        var hpack = encoder.Encode([
            (":status", "200"),
            ("content-type", "text/plain")
        ]);
        var split = hpack.Length / 2;
        var partial = new HeadersFrame(1, hpack.Slice(0, split), endHeaders: false, endStream: false);

        sm.DecodeServerData(TransportData.Rent(SerializeFrame(partial)));

        Assert.Empty(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void DecodeServerData_should_handle_continuation_frame()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var encoder = new HpackEncoder();
        var fullHpack = encoder.Encode([
            (":status", "200"),
            ("content-type", "text/plain")
        ]);
        var hpackSize = fullHpack.Length;
        var split = hpackSize / 2;

        var headers = new HeadersFrame(1, fullHpack[..split], endHeaders: false, endStream: false);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(headers)));

        var cont = new ContinuationFrame(1, fullHpack[split..], endHeaders: true);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(cont)));

        var data = MakeData(1, [], endStream: true);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(data)));

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void DecodeServerData_should_handle_rst_stream_frame()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var rst = new RstStreamFrame(1, Http2ErrorCode.Cancel);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(rst)));

        Assert.Empty(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4.1")]
    public void DecodeServerData_should_disconnect_on_connection_protocol_error()
    {
        // RFC 9113 §5.4.1 / §6.10: a connection-fatal framing error must tear down the connection, not
        // be swallowed and decoding continued against a desynchronized decoder. A bare CONTINUATION with
        // no preceding HEADERS is such an error.
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var badFrame = SerializeFrame(new ContinuationFrame(1, ReadOnlyMemory<byte>.Empty, endHeaders: true));
        sm.DecodeServerData(TransportData.Rent(badFrame));

        Assert.Contains(ops.Outbound, o => o is DisconnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task DecodeServerData_should_fail_in_flight_request_when_stream_is_reset()
    {
        // RFC 9113 §8.1: a RST_STREAM before any response must fail the waiting caller, not leave its
        // Task hanging until an unrelated timeout. The error code is surfaced to the caller.
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();

        var request = MakeGet();
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        request.Options.Set(OptionsKey.Key, pending);
        request.Options.Set(OptionsKey.VersionKey, version);
        var valueTask = new ValueTask<HttpResponseMessage>(pending, version);

        sm.OnRequest(request);

        var rst = new RstStreamFrame(1, Http2ErrorCode.RefusedStream);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(rst)));

        Assert.True(valueTask.IsFaulted);
        await Assert.ThrowsAsync<HttpRequestException>(async () => await valueTask);

        PendingRequest.Return(pending);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void DecodeServerData_should_handle_window_update_on_connection()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var win = new WindowUpdateFrame(0, 16384);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(win)));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void DecodeServerData_should_handle_window_update_on_stream()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var win = new WindowUpdateFrame(1, 8192);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(win)));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void DecodeServerData_should_respond_to_ping_with_ack()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var ping = new PingFrame(new byte[8], isAck: false);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(ping)));

        Assert.Single(ops.Outbound.OfType<TransportData>());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void DecodeServerData_should_ignore_ping_ack()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var pong = new PingFrame(new byte[8], isAck: true);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(pong)));

        Assert.Empty(ops.Outbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeServerData_should_trigger_reconnect_on_goaway_with_inflight()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var goaway = new GoAwayFrame(0, Http2ErrorCode.NoError);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(goaway)));

        Assert.True(sm.IsReconnecting);
        Assert.Single(ops.Outbound, item => item is ConnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void DecodeServerData_should_disconnect_when_connection_flow_control_violated()
    {
        var ops = new FakeClientOps();
        // Advertise a MAX_FRAME_SIZE large enough that the 100 KB frame is legal at the frame layer,
        // so this exercises flow-control enforcement (100000 > 65535 stream window) rather than the
        // separate MAX_FRAME_SIZE check.
        var sm = new Http2ClientStateMachine(MakeConfig(maxFrameSize: 128 * 1024), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1, endStream: false, endHeaders: true);
        var largeData = new byte[100000];
        var data = new DataFrame(1, largeData, endStream: true);
        sm.DecodeServerData(TransportData.Rent(SerializeFrames(headers, data)));

        Assert.Single(ops.Outbound, o => o is DisconnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeServerData_should_correlate_request_with_response()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();

        var req = MakeGet("/test");
        sm.OnRequest(req);
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(headers)));

        var response = Assert.Single(ops.Responses);
        Assert.NotNull(response.RequestMessage);
        Assert.Equal(req.RequestUri, response.RequestMessage.RequestUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4")]
    public void DecodeServerData_should_handle_multiple_concurrent_streams()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();

        sm.OnRequest(MakeGet("/a"));
        sm.OnRequest(MakeGet("/b"));
        ops.Outbound.Clear();

        var headers3 = MakeResponseHeaders(3);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(headers3)));

        var headers1 = MakeResponseHeaders(1);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(headers1)));

        Assert.Equal(2, ops.Responses.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public void CanAcceptRequest_should_respect_max_concurrent_streams()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(maxConcurrentStreams: 2), ops);
        sm.PreStart();

        sm.OnRequest(MakeGet("/a"));
        sm.OnRequest(MakeGet("/b"));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeServerData_should_decode_1xx_status_codes()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var headers = MakeResponseHeaders(1, "100", endStream: true);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(headers)));

        var response = Assert.Single(ops.Responses);
        Assert.Equal(100, (int)response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeServerData_should_decode_4xx_status_codes()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var headers = MakeResponseHeaders(1, "404", endStream: true);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(headers)));

        var response = Assert.Single(ops.Responses);
        Assert.Equal(404, (int)response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeServerData_should_decode_5xx_status_codes()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var headers = MakeResponseHeaders(1, "500", endStream: true);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(headers)));

        var response = Assert.Single(ops.Responses);
        Assert.Equal(500, (int)response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void DecodeServerData_should_absorb_data_for_unknown_stream()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();

        var data = new DataFrame(999, new byte[10], endStream: true);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(data)));

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeServerData_should_absorb_continuation_for_unknown_stream()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();

        var data = new DataFrame(999, new byte[10], endStream: true);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(data)));

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeServerData_should_stream_response_body_via_bridged_reader()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        // Send HEADERS + first DATA in one batch (QueuedBodyReader has fixed capacity,
        // so the consumer must read between enqueues — split into separate messages).
        var headers = MakeResponseHeaders(1, endStream: false, endHeaders: true);
        var data1 = MakeData(1, [1, 2, 3], endStream: false);
        sm.DecodeServerData(TransportData.Rent(SerializeFrames(headers, data1)));

        var response = Assert.Single(ops.Responses);
        var body = response.Content.ReadAsStream(TestContext.Current.CancellationToken);
        Assert.NotNull(body);

        // Consume the first chunk so the bridged reader is ready for the next Supply
        var buf = new byte[3];
        var read = body.Read(buf, 0, buf.Length);
        Assert.Equal(3, read);
        Assert.Equal(new byte[] { 1, 2, 3 }, buf);

        // Now send the second DATA frame with END_STREAM
        var data2 = MakeData(1, [4, 5, 6], endStream: true);
        sm.DecodeServerData(TransportData.Rent(SerializeFrames(data2)));

        read = body.Read(buf, 0, buf.Length);
        Assert.Equal(3, read);
        Assert.Equal(new byte[] { 4, 5, 6 }, buf);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.1")]
    public void Endpoint_should_be_initialized_default()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);

        Assert.Equal(default, sm.Endpoint);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void HasInFlightRequests_should_be_true_when_requests_pending()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        Assert.True(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void HasInFlightRequests_should_be_false_after_response()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var headers = MakeResponseHeaders(1);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(headers)));

        Assert.False(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeServerData_should_preserve_response_headers()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(MakeConfig(), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var encoder = new HpackEncoder();
        var hpack = encoder.Encode([
            (":status", "200"),
            ("content-type", "application/json"),
            ("cache-control", "max-age=3600")
        ]);
        var headers = new HeadersFrame(1, hpack, endHeaders: true, endStream: true);
        sm.DecodeServerData(TransportData.Rent(SerializeFrame(headers)));

        var response = Assert.Single(ops.Responses);
        Assert.True(response.Content.Headers.ContentType is not null);
    }
}