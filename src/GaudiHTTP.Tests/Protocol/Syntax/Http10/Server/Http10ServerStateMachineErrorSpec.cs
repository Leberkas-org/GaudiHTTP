using System.Text;
using Akka.Actor;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http10.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http10.Server;

public sealed class Http10ServerStateMachineErrorSpec : TestKit
{
    private static FakeServerOps MakeOps() => new();

    private static TurboFeatureCollection CreateResponseContext(long contentLength = 0)
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature());
        var responseFeature = new GaudiHttpResponseFeature { StatusCode = 200 };
        if (contentLength > 0)
        {
            responseFeature.Headers["Content-Length"] = contentLength.ToString();
        }

        features.Set<IHttpResponseFeature>(responseFeature);
        var bodyFeature = new GaudiHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return features;
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

        var context = CreateResponseContext(5);
        var bodyFeature = (GaudiHttpResponseBodyFeature)context.Get<IHttpResponseBodyFeature>()!;
        bodyFeature.UpgradeToPipe();
        var bytes = "hello"u8.ToArray();
        await bodyFeature.Writer.WriteAsync(bytes, TestContext.Current.CancellationToken);

        sm.OnResponse(context);

        await bodyFeature.Writer.CompleteAsync();

        // Receive the body message but do NOT dispatch it —
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
    public void OnBodyMessage_DrainReadFailed_should_not_crash_without_prior_response()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);

        var failedMsg = new DrainReadFailed<int>(0, new Exception("Body read failed"));
        var ex = Record.Exception(() => sm.OnBodyMessage(failedMsg));

        Assert.Null(ex);
    }
}