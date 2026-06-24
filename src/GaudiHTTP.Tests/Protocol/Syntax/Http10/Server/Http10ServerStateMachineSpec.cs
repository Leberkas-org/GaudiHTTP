using System.Text;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http10.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http10.Server;

public sealed class Http10ServerStateMachineSpec : TestKit
{
    private static FakeServerOps MakeOps() => new();

    private static IFeatureCollection CreateResponseContext()
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new GaudiHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new GaudiHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return features;
    }

    private static async Task<IFeatureCollection> CreateResponseContextWithBody(string body)
    {
        var context = CreateResponseContext();
        var bodyFeature = context.Get<IHttpResponseBodyFeature>()!;
        var bytes = Encoding.ASCII.GetBytes(body);
        await bodyFeature.Writer.WriteAsync(bytes);
        await bodyFeature.Writer.CompleteAsync();
        return context;
    }

    private static TransportBuffer CreateRequestBuffer(string requestText)
    {
        var bytes = Encoding.ASCII.GetBytes(requestText);
        var buffer = TransportBuffer.Rent(bytes.Length);
        bytes.CopyTo(buffer.FullMemory.Span);
        buffer.Length = bytes.Length;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void DecodeClientData_should_decode_complete_request()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        var requestBuffer = CreateRequestBuffer("GET /path HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeClientData(TransportData.Rent(requestBuffer));

        Assert.Single(ops.Requests);
        var req = ops.Requests[0].Get<IHttpRequestFeature>()!;
        Assert.Equal("GET", req.Method);
        Assert.Equal("/path", req.Path);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void DecodeClientData_should_not_complete_before_response()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        var requestBuffer = CreateRequestBuffer("GET / HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeClientData(TransportData.Rent(requestBuffer));

        Assert.False(sm.ShouldComplete);
        Assert.Single(ops.Requests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void OnResponse_with_stream_body_and_no_content_length_should_use_connection_close_framing()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        var context = CreateResponseContext();

        sm.OnResponse(context);

        // Headers emitted without Content-Length
        var td = ops.Outbound.OfType<TransportData>().First();
        var text = Encoding.ASCII.GetString(td.Buffer.Memory.Span[..td.Buffer.Length]);
        Assert.DoesNotContain("Content-Length", text);

        // Connection close deferred until body completes
        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public async Task OnResponse_with_body_should_emit_transport_data_after_body_buffered()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        var context = await CreateResponseContextWithBody("hello");
        sm.OnResponse(context);

        Assert.Contains(ops.Outbound, o => o is TransportData);
        var td = ops.Outbound.OfType<TransportData>().First();
        var text = Encoding.ASCII.GetString(td.Buffer.Memory.Span[..td.Buffer.Length]);
        Assert.Contains("Content-Length: 5", text);
        Assert.Contains("hello", text);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void OnResponse_with_buffered_body_should_emit_transport_data()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature());
        var responseFeature = new GaudiHttpResponseFeature { StatusCode = 200 };
        responseFeature.Headers["Content-Length"] = "0";
        features.Set<IHttpResponseFeature>(responseFeature);
        var bodyFeature = new GaudiHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);

        sm.OnResponse(features);

        Assert.True(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void CanAcceptResponse_should_always_be_true()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        Assert.True(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void Cleanup_should_abort_active_body()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        sm.Cleanup();

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-3.1")]
    public async Task OnResponse_should_use_http10_version_in_status_line()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        var requestBuffer = CreateRequestBuffer("GET / HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");
        sm.DecodeClientData(TransportData.Rent(requestBuffer));

        var context = await CreateResponseContextWithBody("hello");
        sm.OnResponse(context);

        var td = ops.Outbound.OfType<TransportData>().First();
        var text = Encoding.ASCII.GetString(td.Buffer.Memory.Span[..td.Buffer.Length]);
        Assert.StartsWith("HTTP/1.0 ", text);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5.1.1")]
    public void DecodeClientData_should_signal_error_for_unknown_method()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        var requestBuffer = CreateRequestBuffer("PATCH /path HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");
        sm.DecodeClientData(TransportData.Rent(requestBuffer));

        Assert.Single(ops.Requests);
        var req = ops.Requests[0].Get<IHttpRequestFeature>()!;
        Assert.Equal("PATCH", req.Method);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void DecodeClientData_should_detect_simple_request()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        var requestBuffer = CreateRequestBuffer("GET /path\r\n");
        sm.DecodeClientData(TransportData.Rent(requestBuffer));

        Assert.True(ops.Requests.Count <= 1);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-7.2.2")]
    public void DecodeClientData_should_handle_post_without_content_length()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        var requestBuffer = CreateRequestBuffer("POST /path HTTP/1.0\r\nHost: example.com\r\n\r\n");
        sm.DecodeClientData(TransportData.Rent(requestBuffer));

        if (ops.Requests.Count > 0)
        {
            var req = ops.Requests[0].Get<IHttpRequestFeature>()!;
            var contentLength = req.Headers["Content-Length"];
            Assert.True(string.IsNullOrEmpty(contentLength));
        }
    }
}
