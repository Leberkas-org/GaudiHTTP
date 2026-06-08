using System.Text;
using Akka.Actor;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http10.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Server;

public sealed class Http10ServerStateMachineErrorSpec : TestKit
{
    private static FakeServerOps MakeOps() => new();

    private static IFeatureCollection CreateResponseContext()
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new TurboHttpResponseBodyFeature();
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
    public void DecodeClientData_should_set_ShouldComplete_on_decode_error()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        var requestBuffer = CreateRequestBuffer("POST / HTTP/1.0\r\nContent-Length: abc\r\n\r\n");

        sm.DecodeClientData(TransportData.Rent(requestBuffer));

        Assert.True(sm.ShouldComplete);
        Assert.Empty(ops.Requests);
    }

    [Fact(Timeout = 5000)]
    public void DecodeClientData_should_not_crash_after_prior_decode_error()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        var invalidBuffer = CreateRequestBuffer("POST / HTTP/1.0\r\nContent-Length: abc\r\n\r\n");
        sm.DecodeClientData(TransportData.Rent(invalidBuffer));

        var validBuffer = CreateRequestBuffer("GET / HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");
        var ex = Record.Exception(() => sm.DecodeClientData(TransportData.Rent(validBuffer)));

        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_be_idempotent()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        var ex1 = Record.Exception(() => sm.Cleanup());
        var ex2 = Record.Exception(() => sm.Cleanup());

        Assert.Null(ex1);
        Assert.Null(ex2);
    }

    [Fact(Timeout = 5000)]
    public async Task Cleanup_should_not_throw_when_body_read_in_progress()
    {
        var inbox = Inbox.Create(Sys);
        var ops = new FakeServerOps { StageActor = inbox.Receiver };
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);
        sm.PreStart();

        var context = await CreateResponseContextWithBody("hello");
        sm.OnResponse(context);

        // Receive the first ResponseBodyReadComplete message but do NOT dispatch it —
        // simulates Cleanup arriving while a body read is in-flight.
        await Task.Run(() => inbox.Receive(TimeSpan.FromSeconds(3)));

        var ex = Record.Exception(() => sm.Cleanup());

        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    public void OnBodyMessage_should_ignore_unknown_message_type()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        var ex = Record.Exception(() => sm.OnBodyMessage("unknown message"));

        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    public void OnBodyMessage_ResponseBodyReadFailed_should_not_crash_without_prior_response()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        var failedMsg = new ResponseBodyReadFailed(new Exception("Body read failed"));
        var ex = Record.Exception(() => sm.OnBodyMessage(failedMsg));

        Assert.Null(ex);
    }
}
