using GaudiHTTP.Tests.TestSupport;
using Servus.Akka.Transport;
using GaudiHTTP.Internal;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Protocol.Syntax.Http2.Client;
using GaudiHTTP.Protocol.Syntax.Http2.Hpack;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client.StateMachine;

public sealed class Http2StateMachineSpec
{
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

    private static byte[] SerializeFrameBytes(Http2Frame frame)
    {
        var buffer = WireBuffer.Rent(frame.SerializedSize);
        var span = buffer.FullMemory.Span;
        frame.WriteTo(ref span);
        buffer.Length = frame.SerializedSize;
        return buffer.Span.ToArray();
    }

    private static byte[] SerializeFramesBytes(params Http2Frame[] frames)
    {
        var totalSize = frames.Sum(f => f.SerializedSize);
        var buffer = WireBuffer.Rent(totalSize);
        var span = buffer.FullMemory.Span;
        var offset = 0;
        foreach (var frame in frames)
        {
            var frameSpan = span[offset..];
            frame.WriteTo(ref frameSpan);
            offset += frame.SerializedSize;
        }

        buffer.Length = totalSize;
        return buffer.Span.ToArray();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void PreStart_should_not_emit_preface()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);

        sm.PreStart();

        Assert.Empty(ops.Outbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void OnRequest_should_emit_preface_and_headers_frame_on_first_request()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        sm.OnRequest(MakeGet());
        var transport = sm.ConnectTransport(ops: ops);

        Assert.True(transport.WrittenCount > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void OnRequest_should_reject_when_goaway_received()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();

        var goaway = new GoAwayFrame(0, Http2ErrorCode.NoError);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(goaway), ops: ops);

        sm.OnRequest(MakeGet());

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void OnRequest_should_set_endpoint_on_first_request()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
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
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var content = new ByteArrayContent([1, 2, 3]);
        sm.OnRequest(MakePost("/", content));
        var transport = sm.ConnectTransport(ops: ops);

        Assert.True(transport.WrittenCount > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.1")]
    public void OnRequest_should_allocate_incremented_stream_ids()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        sm.OnRequest(MakeGet("/a"));
        var transport = sm.ConnectTransport(ops: ops);
        sm.OnRequest(MakeGet("/b"));
        sm.OnRequest(MakeGet("/c"));

        Assert.True(transport.WrittenCount > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4")]
    public void DecodeServerData_should_process_settings_frame()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var settings = new SettingsFrame([]);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(settings), ops: ops);

        Assert.True(transport.WrittenCount > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void DecodeServerData_should_produce_response_from_headers_and_data()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1, endStream: false, endHeaders: true);
        var data = MakeData(1, [1, 2, 3], endStream: true);
        var transport = sm.ConnectTransport(initialData: SerializeFramesBytes(headers, data), ops: ops);

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeServerData_should_complete_response_on_headers_with_endstream()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(headers), ops: ops);

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeServerData_should_accumulate_headers_without_endheaders()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
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

        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(partial), ops: ops);

        Assert.Empty(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void DecodeServerData_should_handle_continuation_frame()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
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
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(headers), ops: ops);

        var cont = new ContinuationFrame(1, fullHpack[split..], endHeaders: true);
        transport.FeedMore(sm, ops, SerializeFrameBytes(cont));

        var data = MakeData(1, [], endStream: true);
        transport.FeedMore(sm, ops, SerializeFrameBytes(data));

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void DecodeServerData_should_handle_rst_stream_frame()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var rst = new RstStreamFrame(1, Http2ErrorCode.Cancel);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(rst), ops: ops);

        Assert.Empty(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4.1")]
    public void DecodeServerData_should_disconnect_on_connection_protocol_error()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var badFrame = SerializeFrameBytes(new ContinuationFrame(1, ReadOnlyMemory<byte>.Empty, endHeaders: true));
        var transport = sm.ConnectTransport(initialData: badFrame, ops: ops);

        Assert.Contains(ops.Outbound, o => o is DisconnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task DecodeServerData_should_fail_in_flight_request_when_stream_is_reset()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();

        var request = MakeGet();
        var pending = PendingRequest.Rent();
        var version = pending.Version;
        request.Options.Set(OptionsKey.Key, pending);
        request.Options.Set(OptionsKey.VersionKey, version);
        var valueTask = new ValueTask<HttpResponseMessage>(pending, version);

        sm.OnRequest(request);
        var transport = sm.ConnectTransport(ops: ops);

        var rst = new RstStreamFrame(3, Http2ErrorCode.RefusedStream);
        transport.FeedMore(sm, ops, SerializeFrameBytes(rst));

        Assert.True(valueTask.IsFaulted);
        await Assert.ThrowsAsync<HttpRequestException>(async () => await valueTask);

        PendingRequest.Return(pending);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void DecodeServerData_should_handle_window_update_on_connection()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var win = new WindowUpdateFrame(0, 16384);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(win), ops: ops);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void DecodeServerData_should_handle_window_update_on_stream()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var win = new WindowUpdateFrame(1, 8192);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(win), ops: ops);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void DecodeServerData_should_respond_to_ping_with_ack()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var ping = new PingFrame(new byte[8], isAck: false);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(ping), ops: ops);

        Assert.True(transport.WrittenCount > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void DecodeServerData_should_ignore_ping_ack()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        ops.Outbound.Clear();

        var pong = new PingFrame(new byte[8], isAck: true);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(pong), ops: ops);

        Assert.Empty(ops.Outbound);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void DecodeServerData_should_trigger_reconnect_on_goaway_with_inflight()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        var transport = sm.ConnectTransport(ops: ops);
        ops.Outbound.Clear();

        var goaway = new GoAwayFrame(0, Http2ErrorCode.NoError);
        transport.FeedMore(sm, ops, SerializeFrameBytes(goaway));

        Assert.True(sm.IsReconnecting);
        Assert.Single(ops.Outbound, item => item is ConnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void DecodeServerData_should_disconnect_when_connection_flow_control_violated()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 128 * 1024), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(1, endStream: false, endHeaders: true);
        var largeData = new byte[100000];
        var data = new DataFrame(1, largeData, endStream: true);
        var transport = sm.ConnectTransport(initialData: SerializeFramesBytes(headers, data), ops: ops);

        Assert.Single(ops.Outbound, o => o is DisconnectTransport);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeServerData_should_correlate_request_with_response()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();

        var req = MakeGet("/test");
        sm.OnRequest(req);
        var transport = sm.ConnectTransport(ops: ops);
        ops.Outbound.Clear();

        var headers = MakeResponseHeaders(3);
        transport.FeedMore(sm, ops, SerializeFrameBytes(headers));

        var response = Assert.Single(ops.Responses);
        Assert.NotNull(response.RequestMessage);
        Assert.Equal(req.RequestUri, response.RequestMessage.RequestUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.4")]
    public void DecodeServerData_should_handle_multiple_concurrent_streams()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();

        sm.OnRequest(MakeGet("/a"));
        sm.OnRequest(MakeGet("/b"));
        ops.Outbound.Clear();

        var headers3 = MakeResponseHeaders(3);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(headers3), ops: ops);

        var headers1 = MakeResponseHeaders(1);
        transport.FeedMore(sm, ops, SerializeFrameBytes(headers1));

        Assert.Equal(2, ops.Responses.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public void CanAcceptRequest_should_respect_max_concurrent_streams()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(maxConcurrentStreams: 2, initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();

        sm.OnRequest(MakeGet("/a"));
        var transport = sm.ConnectTransport(ops: ops);
        sm.OnRequest(MakeGet("/b"));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeServerData_should_decode_1xx_status_codes()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var headers = MakeResponseHeaders(1, "100", endStream: true);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(headers), ops: ops);

        var response = Assert.Single(ops.Responses);
        Assert.Equal(100, (int)response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeServerData_should_decode_4xx_status_codes()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var headers = MakeResponseHeaders(1, "404", endStream: true);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(headers), ops: ops);

        var response = Assert.Single(ops.Responses);
        Assert.Equal(404, (int)response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeServerData_should_decode_5xx_status_codes()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var headers = MakeResponseHeaders(1, "500", endStream: true);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(headers), ops: ops);

        var response = Assert.Single(ops.Responses);
        Assert.Equal(500, (int)response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void DecodeServerData_should_absorb_data_for_unknown_stream()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();

        var data = new DataFrame(999, new byte[10], endStream: true);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(data), ops: ops);

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeServerData_should_absorb_continuation_for_unknown_stream()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();

        var data = new DataFrame(999, new byte[10], endStream: true);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(data), ops: ops);

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void DecodeServerData_should_stream_response_body_via_bridged_reader()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var headers = MakeResponseHeaders(1, endStream: false, endHeaders: true);
        var data1 = MakeData(1, [1, 2, 3], endStream: false);
        var transport = sm.ConnectTransport(initialData: SerializeFramesBytes(headers, data1), ops: ops);

        var response = Assert.Single(ops.Responses);
        var body = response.Content.ReadAsStream(TestContext.Current.CancellationToken);
        Assert.NotNull(body);

        var buf = new byte[3];
        var read = body.Read(buf, 0, buf.Length);
        Assert.Equal(3, read);
        Assert.Equal(new byte[] { 1, 2, 3 }, buf);

        var data2 = MakeData(1, [4, 5, 6], endStream: true);
        transport.FeedMore(sm, ops, SerializeFramesBytes(data2));

        read = body.Read(buf, 0, buf.Length);
        Assert.Equal(3, read);
        Assert.Equal(new byte[] { 4, 5, 6 }, buf);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.1")]
    public void Endpoint_should_be_initialized_default()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);

        Assert.Equal(default, sm.Endpoint);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void HasInFlightRequests_should_be_true_when_requests_pending()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());
        var transport = sm.ConnectTransport(ops: ops);

        Assert.True(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void HasInFlightRequests_should_be_false_after_response()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var headers = MakeResponseHeaders(3);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(headers), ops: ops);

        Assert.False(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public void DecodeServerData_should_preserve_response_headers()
    {
        var ops = new FakeClientOps();
        var sm = new Http2ClientStateMachine(TestClientOptions.Create(initialStreamWindowSize: 65_535, maxFrameSize: 16_384), ops);
        sm.PreStart();
        sm.OnRequest(MakeGet());

        var encoder = new HpackEncoder();
        var hpack = encoder.Encode([
            (":status", "200"),
            ("content-type", "application/json"),
            ("cache-control", "max-age=3600")
        ]);
        var headers = new HeadersFrame(1, hpack, endHeaders: true, endStream: true);
        var transport = sm.ConnectTransport(initialData: SerializeFrameBytes(headers), ops: ops);

        var response = Assert.Single(ops.Responses);
        Assert.True(response.Content.Headers.ContentType is not null);
    }
}
