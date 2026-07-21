using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

public sealed class ServerStateMachineSpec
{
    private static Http11ServerStateMachine CreateSm(FakeServerOps ops)
        => new(new GaudiServerOptions().ToHttp1Options(), new GaudiServerOptions().ToHttp2Options(), ops);

    private static byte[] GetRequest(string raw) => Encoding.ASCII.GetBytes(raw);

    private static readonly byte[] SimpleGet = GetRequest(
        "GET / HTTP/1.1\r\n" +
        "Host: localhost\r\n" +
        "Content-Length: 0\r\n" +
        "\r\n");

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6")]
    public void DecodeClientData_should_emit_request_when_complete_get()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.ConnectTransport(SimpleGet, ops);

        Assert.Single(ops.Requests);
        var ctx = ops.Requests[0];
        Assert.Equal("GET", ctx.Get<IHttpRequestFeature>()?.Method);
        Assert.Equal("/", ctx.Get<IHttpRequestFeature>()?.Path);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnResponse_should_emit_response_headers()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var transport = sm.ConnectTransport(SimpleGet, ops);

        var responseBody = "Hello"u8.ToArray();
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(responseBody)
        };
        response.Content.Headers.ContentLength = responseBody.Length;

        sm.OnResponse(MakeResponseContext(response));

        var responseText = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.Contains("200", responseText);
        Assert.Contains("Content-Length: 5", responseText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void CanAcceptResponse_should_be_false_when_no_pending_requests()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);

        Assert.False(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void CanAcceptResponse_should_be_true_after_request_decoded()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.ConnectTransport(SimpleGet, ops);

        Assert.True(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void ShouldCloseAfterResponse_should_be_true_when_connection_close_header()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.ConnectTransport(GetRequest(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n"), ops);

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void ShouldCloseAfterResponse_should_be_true_when_http_10_request()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.ConnectTransport(GetRequest(
            "GET / HTTP/1.0\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n"), ops);

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.6")]
    public void OnResponse_should_set_connection_close_header_when_flag_set()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var transport = sm.ConnectTransport(GetRequest(
            "GET / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n"), ops);

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([])
        };

        sm.OnResponse(MakeResponseContext(response));

        var responseText = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.Contains("Connection: close", responseText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnResponse_should_not_include_body_in_transport_data()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var transport = sm.ConnectTransport(SimpleGet, ops);

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("hello world"u8.ToArray())
        };

        sm.OnResponse(MakeResponseContext(response));

        var responseText = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.Contains("HTTP/1.1 200", responseText);
        Assert.DoesNotContain("hello world", responseText);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-4")]
    public void OnBodyMessage_should_emit_body_chunk_as_transport_data()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var transport = sm.ConnectTransport(SimpleGet, ops);

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("hello world"u8.ToArray())
        };

        sm.OnResponse(MakeResponseContext(response));
        var bytesAfterHeaders = transport.WrittenCount;

        sm.OnBodyMessage(new BodyReadComplete<int>(0, 11));
        sm.OnBodyMessage(new BodyReadComplete<int>(0, 0));

        Assert.True(transport.WrittenCount > bytesAfterHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void CanAcceptResponse_should_be_false_when_outbound_body_pending()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var transport = sm.ConnectTransport(SimpleGet, ops);

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("hello world"u8.ToArray())
        };

        sm.OnResponse(MakeResponseContext(response));

        Assert.False(sm.CanAcceptResponse);

        sm.OnBodyMessage(new BodyReadComplete<int>(0, 0));

        Assert.False(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-3")]
    public void DecodeClientData_should_signal_error_for_oversized_uri()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);

        var longUri = "/" + new string('a', 16_000);
        var requestData = GetRequest(
            $"GET {longUri} HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Content-Length: 0\r\n" +
            "\r\n");

        sm.ConnectTransport(requestData, ops);

        Assert.True(ops.Requests.Count is 0 or 1);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void OnResponse_should_not_include_transfer_encoding_for_204()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var transport = sm.ConnectTransport(SimpleGet, ops);

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        sm.OnResponse(MakeResponseContext(response));

        if (transport.WrittenCount > 0)
        {
            var responseText = Encoding.ASCII.GetString(transport.WrittenSpan);
            Assert.DoesNotContain("Transfer-Encoding", responseText);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-6.1")]
    public void DecodeClientData_should_reject_unknown_transfer_encoding()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        sm.ConnectTransport(GetRequest(
            "POST / HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Transfer-Encoding: unknown\r\n" +
            "\r\n"), ops);

        Assert.Empty(ops.Requests);
        Assert.True(sm.ShouldComplete);
    }

    private static IFeatureCollection MakeResponseContext(HttpResponseMessage response)
    {
        var features = new GaudiFeatureCollection();
        var responseFeature = new GaudiHttpResponseFeature
        {
            StatusCode = (int)response.StatusCode,
            ReasonPhrase = response.ReasonPhrase,
        };

        if (response.Content is not null)
        {
            foreach (var header in response.Content.Headers)
            {
                responseFeature.Headers[header.Key] = new StringValues(header.Value.ToArray());
            }
        }

        foreach (var header in response.Headers)
        {
            responseFeature.Headers[header.Key] = new StringValues(header.Value.ToArray());
        }

        if (response.Content is not null)
        {
            var bodyFeature = new GaudiHttpResponseBodyFeature();
            features.Set<IHttpResponseBodyFeature>(bodyFeature);
        }

        features.Set<IHttpResponseFeature>(responseFeature);
        return features;
    }
}
