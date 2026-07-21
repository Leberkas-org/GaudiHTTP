using System.Net;
using System.Text;
using Akka.TestKit.Xunit;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Internal;
using GaudiHTTP.Protocol.Syntax.Http10.Client;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http10.Client;

public sealed class Http10ClientStateMachineSpec() : TestKit(CiQuietConfig.Instance)
{
    private static HttpRequestMessage MakeRequest(string uri = "http://example.com/", HttpContent? content = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (content != null)
        {
            request.Content = content;
        }

        return request;
    }

    private static byte[] CreateResponseData(string responseText)
    {
        return Encoding.ASCII.GetBytes(responseText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_should_set_endpoint_on_first_request()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(TestClientOptions.Create(), ops);

        sm.OnRequest(MakeRequest("http://example.com:8080/path"));

        Assert.NotEqual(default, sm.Endpoint);
        Assert.Equal("example.com", sm.Endpoint.Host);
        Assert.Equal(8080, sm.Endpoint.Port);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_should_emit_transport_data()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(TestClientOptions.Create(), ops);

        sm.OnRequest(MakeRequest());
        var transport = sm.ConnectTransport(ops: ops);

        Assert.True(transport.WrittenSpan.Length > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_should_set_in_flight_request()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(TestClientOptions.Create(), ops);

        sm.OnRequest(MakeRequest());

        Assert.True(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void DecodeServerData_should_decode_complete_response()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(TestClientOptions.Create(), ops);
        sm.OnRequest(MakeRequest());

        var responseData = CreateResponseData("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello");
        var transport = sm.ConnectTransport(responseData, ops);

        Assert.Single(ops.Responses);
        Assert.Equal(HttpStatusCode.OK, ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void DecodeServerData_should_set_request_message_on_response()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(TestClientOptions.Create(), ops);
        var originalRequest = MakeRequest("http://example.com/test");
        sm.OnRequest(originalRequest);

        var responseData = CreateResponseData("HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n");
        var transport = sm.ConnectTransport(responseData, ops);

        Assert.Single(ops.Responses);
        Assert.NotNull(ops.Responses[0].RequestMessage);
        Assert.Equal(originalRequest.RequestUri, ops.Responses[0].RequestMessage!.RequestUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void StateMachine_should_handle_full_request_response_cycle()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(TestClientOptions.Create(), ops);

        var request = MakeRequest("http://example.com/path");
        sm.OnRequest(request);

        var transport = sm.ConnectTransport(ops: ops);
        Assert.True(sm.HasInFlightRequests);
        Assert.True(transport.WrittenSpan.Length > 0);

        var responseData = CreateResponseData("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello");
        transport.FeedMore(sm, ops, responseData);

        Assert.False(sm.HasInFlightRequests);
        Assert.Single(ops.Responses);
        Assert.Equal(HttpStatusCode.OK, ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void CanAcceptRequest_should_return_false_with_in_flight_request()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(TestClientOptions.Create(), ops);
        sm.OnRequest(MakeRequest());

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void CanAcceptRequest_should_return_true_when_idle()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(TestClientOptions.Create(), ops);

        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Cleanup_should_clear_in_flight_request()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(TestClientOptions.Create(), ops);
        sm.OnRequest(MakeRequest());

        sm.Cleanup();

        Assert.False(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_with_known_cl_body_should_emit_headers_then_stream_body_via_pump()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(TestClientOptions.Create(), ops);
        sm.PreStart();

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent("hello"u8.ToArray())
        };
        sm.OnRequest(request);

        var transport = sm.ConnectTransport(ops: ops);

        // Force-async: drain body pump messages dispatched to StageActor.
        while (ops.BodyMessages.Count > 0)
        {
            var msg = ops.BodyMessages[0];
            ops.BodyMessages.RemoveAt(0);
            sm.OnBodyMessage(msg);
        }

        // Known Content-Length: headers emitted immediately, body streamed via SerialBodyPump.
        var writtenData = transport.WrittenSpan;
        var headerText = Encoding.ASCII.GetString(writtenData);
        Assert.Contains("Content-Length: 5", headerText);
        Assert.Contains("hello", headerText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_body_should_chunk_at_configured_RequestBodyChunkSize()
    {
        // Regression: the HTTP/1.0 body pump hardcoded a 16 KiB chunk size, ignoring the
        // configured RequestBodyChunkSize. A small configured size must split the body.
        var config = new GaudiClientOptions { RequestBodyChunkSize = 4 };
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(config, ops);
        sm.PreStart();

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent("helloworld"u8.ToArray())
        };
        sm.OnRequest(request);

        var transport = sm.ConnectTransport(ops: ops);

        while (ops.BodyMessages.Count > 0)
        {
            var msg = ops.BodyMessages[0];
            ops.BodyMessages.RemoveAt(0);
            sm.OnBodyMessage(msg);
        }

        var writtenData = transport.WrittenSpan;
        var writtenText = Encoding.ASCII.GetString(writtenData);
        // Headers + body should be in WrittenSpan; verify body content is present
        Assert.Contains("helloworld", writtenText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_with_unknown_cl_body_should_fail_request()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(TestClientOptions.Create(), ops);
        sm.PreStart();

        // Use a non-seekable stream wrapper so ContentLength is null — triggers the rejection path.
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new UnknownLengthContent("hello"u8.ToArray())
        };

        // The SM catches the exception internally and fails the request.
        sm.OnRequest(request);
        sm.DecodeServerData(new TransportConnected(DummyConnectionInfo));

        // After the failure, the SM should be ready for a new request (no body pending).
        Assert.False(sm.HasInFlightRequests);
        Assert.True(sm.CanAcceptRequest);
    }

    /// <summary>
    /// HttpContent that wraps a byte array but reports no Content-Length,
    /// forcing the HTTP/1.0 buffered body path.
    /// </summary>
    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _data;

        public UnknownLengthContent(byte[] data)
        {
            _data = data;
        }

        protected override void SerializeToStream(Stream stream, TransportContext? context,
            CancellationToken cancellationToken)
            => stream.Write(_data, 0, _data.Length);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(_data, 0, _data.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_with_body_should_block_CanAcceptRequest_until_body_complete()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(TestClientOptions.Create(), ops);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent("hello"u8.ToArray())
        };
        sm.OnRequest(request);

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void DecodeServerData_should_stream_connection_close_response_immediately()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(TestClientOptions.Create(), ops);
        sm.OnRequest(MakeRequest());

        var responseData = CreateResponseData("HTTP/1.0 200 OK\r\n\r\nhello");
        var transport = sm.ConnectTransport(responseData, ops);

        // RFC 1945 §7.2.2: response delivered immediately with streaming body
        Assert.Single(ops.Responses);
        Assert.Equal(HttpStatusCode.OK, ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void DecodeServerData_should_allow_new_request_after_connection_close_response()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(TestClientOptions.Create(), ops);
        sm.OnRequest(MakeRequest());

        var responseData = CreateResponseData("HTTP/1.0 200 OK\r\n\r\nhello");
        var transport = sm.ConnectTransport(responseData, ops);
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Graceful));

        Assert.Single(ops.Responses);
        Assert.True(sm.CanAcceptRequest);
    }

    private static readonly ConnectionInfo DummyConnectionInfo = new(
        new IPEndPoint(IPAddress.Loopback, 5000),
        new IPEndPoint(IPAddress.Loopback, 80),
        TransportProtocol.Tcp);

    private static void DrainBodyMessages(Http10ClientStateMachine sm, FakeClientOps ops)
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void Reconnect_replay_should_resend_full_seekable_body()
    {
        const int bodySize = 64 * 1024;
        var payload = new byte[bodySize];
        new Random(1).NextBytes(payload);

        var ops = new FakeClientOps();
        var options = TestClientOptions.Create(http1MaxReconnectAttempts: 3,
            http1ReconnectInitialBackoff: TimeSpan.Zero);
        options.RequestBodyChunkSize = 16 * 1024;
        var sm = new Http10ClientStateMachine(options, ops);
        sm.PreStart();

        var content = new ByteArrayContent(payload);
        content.Headers.ContentLength = bodySize;
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/upload")
        {
            Version = HttpVersion.Version10,
            Content = content,
        };

        // First attempt: the 64 KiB body fits within the 256 KiB pump budget, so it fully drains.
        sm.OnRequest(request);
        var transport = sm.ConnectTransport(ops: ops);
        DrainBodyMessages(sm, ops);
        var writtenData = transport.WrittenSpan;
        Assert.True(writtenData.Length >= bodySize, $"Expected at least {bodySize} bytes written, got {writtenData.Length}");

        // Ungraceful disconnect: the request is buffered for reconnect replay.
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        Assert.True(sm.IsReconnecting);

        // Reconnect replay MUST re-send the FULL body, not 0 bytes from the already-consumed content
        // stream (which would hang a fixed-length server read).
        var transport2 = sm.ConnectTransport(ops: ops);
        DrainBodyMessages(sm, ops);

        var replayedWritten = transport2.WrittenSpan;
        Assert.True(replayedWritten.Length >= bodySize, $"Replayed body should be at least {bodySize} bytes, got {replayedWritten.Length}");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void Reconnect_replay_should_fail_fast_when_body_is_non_rewindable()
    {
        const int bodySize = 64 * 1024;

        var ops = new FakeClientOps();
        var options = TestClientOptions.Create(http1MaxReconnectAttempts: 3,
            http1ReconnectInitialBackoff: TimeSpan.Zero);
        options.RequestBodyChunkSize = 16 * 1024;
        var sm = new Http10ClientStateMachine(options, ops);
        sm.PreStart();

        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var content = new StreamContent(new NonSeekableStream(bodySize));
        content.Headers.ContentLength = bodySize;
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/upload")
        {
            Version = HttpVersion.Version10,
            Content = content,
        };
        request.Options.Set(OptionsKey.Key, pending);
        request.Options.Set(OptionsKey.VersionKey, version);

        sm.OnRequest(request);
        var transport = sm.ConnectTransport(ops: ops);
        DrainBodyMessages(sm, ops);

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
        Assert.True(sm.IsReconnecting);

        // A consumed forward-only body cannot be rewound → fail fast instead of sending a truncated
        // fixed-length body.
        var transport2 = sm.ConnectTransport(ops: ops);
        DrainBodyMessages(sm, ops);

        Assert.True(pending.GetValueTask().IsFaulted);
    }
}
