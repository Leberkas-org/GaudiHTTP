using System.Net;
using System.Text;
using Akka.Actor;
using Akka.TestKit.Xunit;
using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Protocol.Syntax.Http10.Client;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Client;

public sealed class Http10ClientStateMachineSpec : TestKit
{
    private static TurboClientOptions MakeConfig() => new();

    private static HttpRequestMessage MakeRequest(string uri = "http://example.com/", HttpContent? content = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (content != null)
        {
            request.Content = content;
        }

        return request;
    }

    private static TransportBuffer CreateResponseBuffer(string responseText)
    {
        var bytes = Encoding.ASCII.GetBytes(responseText);
        var buffer = TransportBuffer.Rent(bytes.Length);
        bytes.CopyTo(buffer.FullMemory.Span);
        buffer.Length = bytes.Length;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_should_set_endpoint_on_first_request()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(ops, MakeConfig());

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
        var sm = new Http10ClientStateMachine(ops, MakeConfig());

        sm.OnRequest(MakeRequest());

        Assert.Contains(ops.Outbound, o => o is TransportData);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_should_set_in_flight_request()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(ops, MakeConfig());

        sm.OnRequest(MakeRequest());

        Assert.True(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void DecodeServerData_should_decode_complete_response()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var responseBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello");

        sm.DecodeServerData(TransportData.Rent(responseBuffer));

        Assert.Single(ops.Responses);
        Assert.Equal(HttpStatusCode.OK, ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-6")]
    public void DecodeServerData_should_set_request_message_on_response()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(ops, MakeConfig());
        var originalRequest = MakeRequest("http://example.com/test");
        sm.OnRequest(originalRequest);

        var responseBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeServerData(TransportData.Rent(responseBuffer));

        Assert.Single(ops.Responses);
        Assert.NotNull(ops.Responses[0].RequestMessage);
        Assert.Equal(originalRequest.RequestUri, ops.Responses[0].RequestMessage!.RequestUri);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void StateMachine_should_handle_full_request_response_cycle()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(ops, MakeConfig());

        var request = MakeRequest("http://example.com/path");
        sm.OnRequest(request);

        Assert.True(sm.HasInFlightRequests);
        Assert.Contains(ops.Outbound, o => o is TransportData);

        ops.Outbound.Clear();

        var responseBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello");
        sm.DecodeServerData(TransportData.Rent(responseBuffer));

        Assert.False(sm.HasInFlightRequests);
        Assert.Single(ops.Responses);
        Assert.Equal(HttpStatusCode.OK, ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void CanAcceptRequest_should_return_false_with_in_flight_request()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void CanAcceptRequest_should_return_true_when_idle()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(ops, MakeConfig());

        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-8")]
    public void Cleanup_should_clear_in_flight_request()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        sm.Cleanup();

        Assert.False(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void OnRequest_with_known_cl_body_should_emit_headers_then_stream_body_via_pump()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(ops, MakeConfig());
        sm.PreStart();

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent("hello"u8.ToArray())
        };
        sm.OnRequest(request);

        // Known Content-Length: headers emitted immediately, body streamed via SerialBodyPump.
        // The pump reads synchronously from ByteArrayContent and calls EmitDataFrames inline,
        // so headers + body chunks + endStream all happen within OnRequest.
        var transportData = ops.Outbound.OfType<TransportData>().ToList();
        Assert.True(transportData.Count >= 2, "Expected headers + at least one body TransportData");

        var headerText = Encoding.ASCII.GetString(transportData[0].Buffer.Memory.Span[..transportData[0].Buffer.Length]);
        Assert.Contains("Content-Length: 5", headerText);

        // Body data arrives in subsequent TransportData messages
        var bodyBytes = transportData.Skip(1)
            .SelectMany(td => td.Buffer.Memory.Span[..td.Buffer.Length].ToArray())
            .ToArray();
        var bodyText = Encoding.ASCII.GetString(bodyBytes);
        Assert.Contains("hello", bodyText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public async Task OnRequest_with_unknown_cl_body_should_emit_transport_data_after_body_buffered()
    {
        var inbox = Inbox.Create(Sys);
        var ops = new FakeClientOps { StageActor = inbox.Receiver };
        var sm = new Http10ClientStateMachine(ops, MakeConfig());
        sm.PreStart();

        // Use a non-seekable stream wrapper so ContentLength is null — forces the buffered path.
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new UnknownLengthContent("hello"u8.ToArray())
        };
        sm.OnRequest(request);

        Assert.DoesNotContain(ops.Outbound, o => o is TransportData);

        // SM uses BufferedBodyWriter + PipeTo — will send BodyReadComplete(5) then BodyReadComplete(0)
        // then BodyBufferComplete once fully buffered
        var msg1 = await Task.Run(() => inbox.Receive(TimeSpan.FromSeconds(3)));
        sm.OnBodyMessage(msg1);

        var msg2 = await Task.Run(() => inbox.Receive(TimeSpan.FromSeconds(3)));
        sm.OnBodyMessage(msg2);

        // BodyBufferComplete triggers EncodeDeferred → TransportData
        var msg3 = await Task.Run(() => inbox.Receive(TimeSpan.FromSeconds(3)));
        sm.OnBodyMessage(msg3);

        Assert.Contains(ops.Outbound, o => o is TransportData);
        var td = ops.Outbound.OfType<TransportData>().First();
        var text = Encoding.ASCII.GetString(td.Buffer.Memory.Span[..td.Buffer.Length]);
        Assert.Contains("Content-Length: 5", text);
        Assert.Contains("hello", text);
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
        var sm = new Http10ClientStateMachine(ops, MakeConfig());

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent("hello"u8.ToArray())
        };
        sm.OnRequest(request);

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void DecodeServerData_should_complete_connection_close_response_on_graceful_disconnect()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var headerBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\n\r\nhello");
        sm.DecodeServerData(TransportData.Rent(headerBuffer));

        Assert.Empty(ops.Responses);

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Graceful));

        Assert.Single(ops.Responses);
        Assert.Equal(HttpStatusCode.OK, ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7")]
    public void DecodeServerData_should_allow_new_request_after_connection_close_response()
    {
        var ops = new FakeClientOps();
        var sm = new Http10ClientStateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        var headerBuffer = CreateResponseBuffer("HTTP/1.0 200 OK\r\n\r\nhello");
        sm.DecodeServerData(TransportData.Rent(headerBuffer));
        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Graceful));

        Assert.Single(ops.Responses);
        Assert.True(sm.CanAcceptRequest);
    }
}
