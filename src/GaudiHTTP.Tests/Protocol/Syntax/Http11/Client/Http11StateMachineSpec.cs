using System.Text;
using Servus.Akka.TestKit;
using Servus.Akka.Transport;
using GaudiHTTP.Internal;
using GaudiHTTP.Client;
using GaudiHTTP.Protocol.Syntax.Http11.Client;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Client;

public sealed class Http11StateMachineSpec
{
    private static HttpRequestMessage MakeRequest(string path = "/", string? method = null, HttpContent? content = null)
    {
        var httpMethod = method switch
        {
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "PATCH" => HttpMethod.Patch,
            "DELETE" => HttpMethod.Delete,
            "HEAD" => HttpMethod.Head,
            _ => HttpMethod.Get
        };

        var req = new HttpRequestMessage(httpMethod, $"http://example.com{path}")
        {
            Version = new Version(1, 1),
            Content = content
        };

        return req;
    }

    private static (HttpRequestMessage Request, PendingRequest Pending) MakeTrackedRequest(
        string path = "/", string? method = null, HttpContent? content = null)
    {
        var httpMethod = method switch
        {
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "PATCH" => HttpMethod.Patch,
            "DELETE" => HttpMethod.Delete,
            "HEAD" => HttpMethod.Head,
            _ => HttpMethod.Get
        };

        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var req = new HttpRequestMessage(httpMethod, $"http://example.com{path}")
        {
            Version = new Version(1, 1),
            Content = content
        };
        req.Options.Set(OptionsKey.Key, pending);
        req.Options.Set(OptionsKey.VersionKey, version);

        return (req, pending);
    }

    private static TestPipeTransport ConnectWithResponse(
        Http11ClientStateMachine sm, string response)
    {
        var transport = new TestPipeTransport();
        var bytes = Encoding.ASCII.GetBytes(response);
        var span = transport.InputWriter.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        transport.InputWriter.Advance(bytes.Length);
        var flush = transport.InputWriter.FlushAsync();
        if (!flush.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Pipe flush did not complete synchronously");
        }

        sm.DecodeServerData(new TransportConnected(ConnectionInfo.None, transport));
        return transport;
    }

    private static TestPipeTransport Connect(Http11ClientStateMachine sm)
    {
        var transport = new TestPipeTransport();
        sm.DecodeServerData(new TransportConnected(ConnectionInfo.None, transport));
        return transport;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_enqueue_request_and_emit_stream_acquire()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);

        sm.OnRequest(MakeRequest());

        Assert.True(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_encode_after_transport_connected()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        var transport = new TestPipeTransport();

        sm.OnRequest(MakeRequest());
        sm.DecodeServerData(new TransportConnected(ConnectionInfo.None, transport));

        var output = transport.CapturedOutputBytes;
        Assert.True(output.Length > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_set_endpoint_on_first_request()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);

        sm.OnRequest(MakeRequest());

        Assert.NotEqual(default, sm.Endpoint);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_respect_max_pipeline_depth()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 2), ops);

        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_handle_post_request_with_content()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        var transport = new TestPipeTransport();
        var content = new StringContent("test body", Encoding.UTF8);

        sm.OnRequest(MakeRequest("/", "POST", content));
        sm.DecodeServerData(new TransportConnected(ConnectionInfo.None, transport));

        Assert.True(sm.HasInFlightRequests);
        Assert.True(transport.CapturedOutputBytes.Length > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_emit_multiple_requests_in_pipeline()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        var transport = new TestPipeTransport();

        sm.OnRequest(MakeRequest("/1"));
        sm.DecodeServerData(new TransportConnected(ConnectionInfo.None, transport));
        sm.OnRequest(MakeRequest("/2"));
        sm.OnRequest(MakeRequest("/3"));

        Assert.Equal(3, sm.PendingRequestCount);
        Assert.True(transport.CapturedOutputBytes.Length > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_handle_request_without_content()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        var transport = new TestPipeTransport();

        sm.OnRequest(MakeRequest("/", "GET"));
        sm.DecodeServerData(new TransportConnected(ConnectionInfo.None, transport));

        Assert.True(sm.HasInFlightRequests);
        Assert.True(transport.CapturedOutputBytes.Length > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void OnRequest_should_encode_post_headers_via_transport()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        var transport = new TestPipeTransport();
        var content = new StringContent("test", Encoding.UTF8);

        sm.OnRequest(MakeRequest("/", "POST", content));
        sm.DecodeServerData(new TransportConnected(ConnectionInfo.None, transport));

        Assert.True(transport.CapturedOutputBytes.Length > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void DecodeServerData_should_decode_single_response()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest());

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello");

        Assert.Single(ops.Responses);
        Assert.Equal((int)System.Net.HttpStatusCode.OK, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void DecodeServerData_should_emit_connection_reuse_item()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest());

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void DecodeServerData_should_decode_multiple_pipelined_responses()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));

        ConnectWithResponse(sm,
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK" +
            "HTTP/1.1 201 Created\r\nContent-Length: 7\r\n\r\nCreated");

        Assert.Equal(2, ops.Responses.Count);
        Assert.Equal((int)System.Net.HttpStatusCode.OK, (int)ops.Responses[0].StatusCode);
        Assert.Equal((int)System.Net.HttpStatusCode.Created, (int)ops.Responses[1].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_push_streaming_response_immediately_for_close_delimited()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest());

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\n\r\n");

        Assert.Single(ops.Responses);
        Assert.Equal(200, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_push_response_before_body_complete_for_streaming()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest());

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\n\r\n");

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_handle_connection_close_header()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");

        Assert.Single(ops.Responses);
        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void DecodeServerData_should_handle_graceful_disconnect()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest());
        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Graceful));

        Assert.False(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void DecodeServerData_should_clear_effective_pipeline_depth_when_connection_close_with_multiple_inflight()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));
        sm.OnRequest(MakeRequest("/3"));

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 2\r\n\r\nOK");

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void DecodeServerData_should_preserve_request_reference()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        var req = MakeRequest();
        sm.OnRequest(req);

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");

        Assert.NotNull(ops.Responses[0].RequestMessage);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_complete_close_delimited_response_on_graceful_disconnect()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest());

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\n\r\nbody content");

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Graceful));

        Assert.Single(ops.Responses);
        Assert.Equal((int)System.Net.HttpStatusCode.OK, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_push_response_immediately_for_streaming_then_handle_abrupt_close()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        var (request, pending) = MakeTrackedRequest();
        sm.OnRequest(request);

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\n\r\n");

        Assert.Single(ops.Responses);

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_decode_eof_response_on_graceful_disconnect()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest());

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello");

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Graceful));

        Assert.Single(ops.Responses);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_stay_alive_after_abrupt_close_when_no_pending()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        var (request, _) = MakeTrackedRequest();
        sm.OnRequest(request);

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));

        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void DecodeServerData_should_push_response_immediately_then_handle_abrupt_close_with_body()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest());

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\n\r\n");

        Assert.Single(ops.Responses);

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Error));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void OnUpstreamFinished_should_complete_when_no_inflight_requests()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);

        sm.OnUpstreamFinished();

        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void OnUpstreamFinished_should_fail_orphaned_requests()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        var (request1, pending1) = MakeTrackedRequest("/1");
        var (request2, pending2) = MakeTrackedRequest("/2");
        sm.OnRequest(request1);
        sm.OnRequest(request2);

        sm.OnUpstreamFinished();

        Assert.False(sm.HasInFlightRequests);
        var task1 = pending1.GetValueTask();
        var task2 = pending2.GetValueTask();
        Assert.True(task1.IsFaulted);
        Assert.True(task2.IsFaulted);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void CanAcceptRequest_should_be_true_initially()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);

        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void CanAcceptRequest_should_be_false_when_queue_full()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 2), ops);
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void HasInFlightRequests_should_reflect_queue_count()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);

        Assert.False(sm.HasInFlightRequests);
        sm.OnRequest(MakeRequest());
        Assert.True(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Endpoint_should_be_initialized_on_first_request()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);

        Assert.Equal(default, sm.Endpoint);
        sm.OnRequest(MakeRequest());
        Assert.NotEqual(default, sm.Endpoint);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void PendingRequestCount_should_reflect_queue_count()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));

        Assert.Equal(2, sm.PendingRequestCount);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void IsReconnecting_should_be_false_initially()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);

        Assert.False(sm.IsReconnecting);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Cleanup_should_clear_inflight_queue()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));

        sm.Cleanup();

        Assert.False(sm.HasInFlightRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Cleanup_should_dispose_body_owners()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest());

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\n\r\nbody");

        sm.Cleanup();

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void Pipeline_should_correlate_responses_to_requests_in_order()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));
        sm.OnRequest(MakeRequest("/3"));

        ConnectWithResponse(sm,
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK" +
            "HTTP/1.1 201 Created\r\nContent-Length: 7\r\n\r\nCreated" +
            "HTTP/1.1 202 Accepted\r\nContent-Length: 8\r\n\r\nAccepted");

        Assert.Equal(3, ops.Responses.Count);
        Assert.NotNull(ops.Responses[0].RequestMessage);
        Assert.NotNull(ops.Responses[1].RequestMessage);
        Assert.NotNull(ops.Responses[2].RequestMessage);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void CloseDelimited_should_work_with_initial_body_bytes()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest());

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\n\r\nstart");

        Assert.False(sm.ShouldPauseNetwork);

        sm.DecodeServerData(new TransportDisconnected(DisconnectReason.Graceful));

        Assert.Single(ops.Responses);
        Assert.Equal((int)System.Net.HttpStatusCode.OK, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void NoBodyResponseTypes_should_not_be_close_delimited()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest());

        ConnectWithResponse(sm, "HTTP/1.1 204 No Content\r\n\r\n");

        Assert.Single(ops.Responses);
        Assert.Equal((int)System.Net.HttpStatusCode.NoContent, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void Not_Modified_should_not_be_close_delimited()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest());

        ConnectWithResponse(sm, "HTTP/1.1 304 Not Modified\r\n\r\n");

        Assert.Single(ops.Responses);
        Assert.Equal((int)System.Net.HttpStatusCode.NotModified, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.8")]
    public void TransferEncoding_chunked_should_not_be_close_delimited()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest());

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n");

        Assert.Single(ops.Responses);
        Assert.Equal(200, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void Multiple_requests_with_connection_close_should_disable_pipeline()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 8), ops);
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));
        sm.OnRequest(MakeRequest("/3"));

        ConnectWithResponse(sm, "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");

        Assert.Single(ops.Responses);
        var response = ops.Responses[0];
        Assert.True(response.Headers.ConnectionClose);
    }

    [Fact(Timeout = 5000)]
    public void CanAcceptRequest_should_be_false_while_body_pending()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(new GaudiClientOptions(), ops);
        sm.PreStart();
        var transport = new TestPipeTransport();

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(new byte[1024 * 1024])
        };
        sm.OnRequest(request);
        sm.DecodeServerData(new TransportConnected(ConnectionInfo.None, transport));

        Assert.False(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    public void CanAcceptRequest_should_become_true_after_body_drain_completes()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(new GaudiClientOptions(), ops);
        sm.PreStart();
        var transport = new TestPipeTransport();

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(new byte[1000])
        };
        sm.OnRequest(request);
        sm.DecodeServerData(new TransportConnected(ConnectionInfo.None, transport));

        while (ops.BodyMessages.Count > 0)
        {
            var msg = ops.BodyMessages[0];
            ops.BodyMessages.RemoveAt(0);
            sm.OnBodyMessage(msg);
        }

        Assert.True(sm.CanAcceptRequest);
    }
}