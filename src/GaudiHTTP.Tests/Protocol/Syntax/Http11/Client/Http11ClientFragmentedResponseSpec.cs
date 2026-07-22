using System.Net;
using System.Text;
using Servus.Akka.TestKit;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http11.Client;
using GaudiHTTP.Tests.Shared;
using GaudiHTTP.Tests.TestSupport;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Client;

/// <summary>
/// A single H1.1 connection delivers responses in TCP-sized reads, so a response's status line or
/// header block can be split across two reads. Each read is a separate transport buffer that the
/// state machine disposes after feeding it. The decoder must retain the unconsumed prefix and
/// resume from it on the next read — otherwise the partial bytes are lost and the continuation is
/// parsed as garbage ("Malformed header field"), desyncing the connection. This was the trigger for
/// the intermittent single-connection pipelining deadlock.
///
/// Uses <see cref="TestPipeTransport"/> (real pipes) rather than <see cref="InMemoryTransport"/>
/// because pipe transport preserves unconsumed bytes across reads via AdvanceTo, matching real TCP
/// semantics where a partial header survives until the continuation arrives.
/// </summary>
public sealed class Http11ClientFragmentedResponseSpec
{
    private static HttpRequestMessage MakeRequest(string path = "/")
        => new(HttpMethod.Get, $"http://example.com{path}") { Version = new Version(1, 1) };

    private static TestPipeTransport ConnectWithFragment(
        Http11ClientStateMachine sm, FakeClientOps ops, string fragment)
    {
        var transport = new TestPipeTransport();
        var bytes = Encoding.ASCII.GetBytes(fragment);
        var span = transport.InputWriter.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        transport.InputWriter.Advance(bytes.Length);
        var flush = transport.InputWriter.FlushAsync();
        if (!flush.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Pipe flush did not complete synchronously");
        }

        sm.DecodeServerData(new TransportConnected(ConnectionInfo.None, transport));
        DrainBodyMessages(sm, ops);
        return transport;
    }

    private static void FeedFragment(
        TestPipeTransport transport, Http11ClientStateMachine sm, FakeClientOps ops, string fragment)
    {
        var bytes = Encoding.ASCII.GetBytes(fragment);
        var span = transport.InputWriter.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        transport.InputWriter.Advance(bytes.Length);
        var flush = transport.InputWriter.FlushAsync();
        if (!flush.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Pipe flush did not complete synchronously");
        }

        DrainBodyMessages(sm, ops);
    }

    private static void DrainBodyMessages(Http11ClientStateMachine sm, FakeClientOps ops)
    {
        SpinWait.SpinUntil(() => ops.BodyMessages.Count > 0, TimeSpan.FromSeconds(2));
        while (ops.BodyMessages.Count > 0)
        {
            var snapshot = ops.BodyMessages.ToArray();
            ops.BodyMessages.Clear();
            foreach (var msg in snapshot)
            {
                sm.OnBodyMessage(msg);
            }

            if (ops.BodyMessages.Count == 0)
            {
                Thread.Sleep(1);
                SpinWait.SpinUntil(() => ops.BodyMessages.Count > 0, TimeSpan.FromMilliseconds(50));
            }
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-2.2")]
    public void DecodeServerData_should_decode_response_when_header_line_split_across_two_reads()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 256), ops);
        sm.OnRequest(MakeRequest());

        const string full = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\nOK";
        const int split = 35; // mid "Content-Type: appl|ication/json"

        var transport = ConnectWithFragment(sm, ops, full[..split]);
        FeedFragment(transport, sm, ops, full[split..]);

        Assert.Single(ops.Responses);
        Assert.Equal((int)HttpStatusCode.OK, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-2.2")]
    public void DecodeServerData_should_decode_response_when_status_line_split_across_two_reads()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 256), ops);
        sm.OnRequest(MakeRequest());

        const string full = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK";
        const int split = 11; // mid "HTTP/1.1 20|0 OK"

        var transport = ConnectWithFragment(sm, ops, full[..split]);
        FeedFragment(transport, sm, ops, full[split..]);

        Assert.Single(ops.Responses);
        Assert.Equal((int)HttpStatusCode.OK, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void DecodeServerData_should_decode_second_pipelined_response_when_split_after_first()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(TestClientOptions.Create(maxPipelineDepth: 256), ops);
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));

        // First response complete; second response's headers split across the read boundary.
        const string full =
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK" +
            "HTTP/1.1 201 Created\r\nContent-Length: 7\r\n\r\nCreated";
        const int split = 55; // somewhere inside the second response's header block

        var transport = ConnectWithFragment(sm, ops, full[..split]);
        FeedFragment(transport, sm, ops, full[split..]);

        Assert.Equal(2, ops.Responses.Count);
        Assert.Equal((int)HttpStatusCode.OK, (int)ops.Responses[0].StatusCode);
        Assert.Equal((int)HttpStatusCode.Created, (int)ops.Responses[1].StatusCode);
    }
}
