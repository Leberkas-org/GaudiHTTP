using System.Net;
using System.Text;
using Servus.Akka.Transport;
using GaudiHTTP.Client;
using GaudiHTTP.Protocol.Syntax.Http11.Client;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Client;

/// <summary>
/// A single H1.1 connection delivers responses in TCP-sized reads, so a response's status line or
/// header block can be split across two reads. Each read is a separate transport buffer that the
/// state machine disposes after feeding it. The decoder must retain the unconsumed prefix and
/// resume from it on the next read — otherwise the partial bytes are lost and the continuation is
/// parsed as garbage ("Malformed header field"), desyncing the connection. This was the trigger for
/// the intermittent single-connection pipelining deadlock.
/// </summary>
public sealed class Http11ClientFragmentedResponseSpec
{
    private static TurboClientOptions MakeConfig()
        => new() { Http1 = new Http1ClientOptions { MaxPipelineDepth = 256 } };

    private static HttpRequestMessage MakeRequest(string path = "/")
        => new(HttpMethod.Get, $"http://example.com{path}") { Version = new Version(1, 1) };

    private static TransportBuffer Buf(string s)
    {
        var bytes = Encoding.ASCII.GetBytes(s);
        var buffer = TransportBuffer.Rent(bytes.Length);
        bytes.CopyTo(buffer.FullMemory.Span);
        buffer.Length = bytes.Length;
        return buffer;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-2.2")]
    public void DecodeServerData_should_decode_response_when_header_line_split_across_two_reads()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        const string full = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\nOK";
        const int split = 35; // mid "Content-Type: appl|ication/json"

        sm.DecodeServerData(TransportData.Rent(Buf(full[..split])));
        sm.DecodeServerData(TransportData.Rent(Buf(full[split..])));

        Assert.Single(ops.Responses);
        Assert.Equal((int)HttpStatusCode.OK, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-2.2")]
    public void DecodeServerData_should_decode_response_when_status_line_split_across_two_reads()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest());

        const string full = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK";
        const int split = 11; // mid "HTTP/1.1 20|0 OK"

        sm.DecodeServerData(TransportData.Rent(Buf(full[..split])));
        sm.DecodeServerData(TransportData.Rent(Buf(full[split..])));

        Assert.Single(ops.Responses);
        Assert.Equal((int)HttpStatusCode.OK, (int)ops.Responses[0].StatusCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void DecodeServerData_should_decode_second_pipelined_response_when_split_after_first()
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(ops, MakeConfig());
        sm.OnRequest(MakeRequest("/1"));
        sm.OnRequest(MakeRequest("/2"));

        // First response complete; second response's headers split across the read boundary.
        const string full =
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nOK" +
            "HTTP/1.1 201 Created\r\nContent-Length: 7\r\n\r\nCreated";
        const int split = 55; // somewhere inside the second response's header block

        sm.DecodeServerData(TransportData.Rent(Buf(full[..split])));
        sm.DecodeServerData(TransportData.Rent(Buf(full[split..])));

        Assert.Equal(2, ops.Responses.Count);
        Assert.Equal((int)HttpStatusCode.OK, (int)ops.Responses[0].StatusCode);
        Assert.Equal((int)HttpStatusCode.Created, (int)ops.Responses[1].StatusCode);
    }
}
