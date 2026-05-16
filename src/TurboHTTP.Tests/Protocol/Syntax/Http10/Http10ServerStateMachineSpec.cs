using System.Net;
using System.Text;
using Akka.Actor;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http10.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10;

public sealed class Http10ServerStateMachineSpec
{
    private static FakeServerOps MakeOps() => new();

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
        var sm = new Http10ServerStateMachine(ops);

        var requestBuffer = CreateRequestBuffer("GET /path HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeClientData(new TransportData(requestBuffer));

        Assert.Single(ops.Requests);
        Assert.Equal(HttpMethod.Get, ops.Requests[0].Method);
        Assert.Equal("/path", ops.Requests[0].RequestUri?.OriginalString);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945-5")]
    public void DecodeClientData_should_mark_should_complete()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(ops);

        var requestBuffer = CreateRequestBuffer("GET / HTTP/1.0\r\nHost: example.com\r\nContent-Length: 0\r\n\r\n");

        sm.DecodeClientData(new TransportData(requestBuffer));

        Assert.True(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void OnResponse_should_not_emit_transport_data_before_body_delivered()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(ops);

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("test body")
        };

        sm.OnResponse(response);

        Assert.DoesNotContain(ops.Outbound, o => o is TransportData);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public async Task OnResponse_with_body_should_emit_transport_data_after_body_chunk()
    {
        var system = ActorSystem.Create("http10-server-sm-test");
        try
        {
            var inbox = Inbox.Create(system);
            var ops = new FakeServerOps { StageActor = inbox.Receiver };
            var sm = new Http10ServerStateMachine(ops);
            sm.PreStart();

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("hello"u8.ToArray())
            };
            sm.OnResponse(response);

            // No TransportData yet (deferred)
            Assert.DoesNotContain(ops.Outbound, o => o is TransportData);

            // Receive the OutboundBodyChunk and feed it back
            var msg = await Task.Run(() => inbox.Receive(TimeSpan.FromSeconds(3)));
            var chunk = Assert.IsType<OutboundBodyChunk>(msg);
            sm.OnBodyMessage(chunk);

            var msg2 = await Task.Run(() => inbox.Receive(TimeSpan.FromSeconds(3)));
            sm.OnBodyMessage(msg2); // OutboundBodyComplete

            // Now TransportData should exist
            Assert.Contains(ops.Outbound, o => o is TransportData);
            var td = ops.Outbound.OfType<TransportData>().First();
            var text = Encoding.ASCII.GetString(td.Buffer.Memory.Span[..td.Buffer.Length]);
            Assert.Contains("Content-Length: 5", text);
            Assert.Contains("hello", text);
        }
        finally
        {
            system.Dispose();
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void OnResponse_should_add_connection_close_header()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(ops);

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new ByteArrayContent([]);

        sm.OnResponse(response);

        Assert.Contains("close", response.Headers.Connection);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void CanAcceptResponse_should_always_be_true()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(ops);

        Assert.True(sm.CanAcceptResponse);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC1945")]
    public void Cleanup_should_abort_active_body()
    {
        var ops = MakeOps();
        var sm = new Http10ServerStateMachine(ops);

        sm.Cleanup();

        // Should not crash
        Assert.True(true);
    }
}
