using System.Text;
using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Protocol.Syntax.Http11.Client;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Client;

/// <summary>
/// Characterizes the H1.1 client RECEIVE-side back-pressure latch surfaced by the single-connection
/// download analysis (#4). A STREAMED (chunked) response body fills the <see cref="Protocol.Body.QueuedBodyReader"/>
/// and latches <c>ShouldPauseNetwork</c>, which gates every socket <c>Pull(_inServer)</c>. The ONLY
/// path that releases the latch is the application reading the body
/// (<c>QueuedBodyReader.AdvanceTo → SlotFreed</c>).
///
/// LATENT DEFECT (first test): the caller's other natural action — <c>using var response</c>, i.e.
/// disposing the response WITHOUT reading the body — does NOT release the latch, because
/// <c>QueuedBodyStream</c> has no <c>Dispose</c> override. On a single H1.1 connection (MaxConnections=1)
/// the connection then stays paused forever and every pipelined sibling is stranded. The fix is to make
/// disposing the body drain/cancel the reader (and drain the rest of the body off the wire) so the
/// connection can resume.
///
/// NOTE: this is NOT the cause of the benchmark's H1.1 hang — that workload's response is a 3-byte
/// Content-Length body (buffered path), confirmed by curl. This spec guards the streaming path only.
/// </summary>
public sealed class Http11ClientReceiveBackpressureSpec
{
    private static TransportData Inbound(string ascii)
    {
        var bytes = Encoding.ASCII.GetBytes(ascii);
        var buf = TransportBuffer.Rent(bytes.Length);
        bytes.CopyTo(buf.FullMemory.Span);
        buf.Length = bytes.Length;
        return TransportData.Rent(buf);
    }

    // A chunked response with `chunks` 4-byte chunks, deliberately NOT terminated (no "0\r\n\r\n"),
    // so the body stays mid-stream with the receive queue full.
    private static string ChunkedResponse(int chunks)
    {
        var sb = new StringBuilder("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n");
        for (var i = 0; i < chunks; i++)
        {
            sb.Append("4\r\nDATA\r\n");
        }

        return sb.ToString();
    }

    private static (Http11ClientStateMachine Sm, FakeClientOps Ops) NewClientWithStreamingResponse(int chunks)
    {
        var ops = new FakeClientOps();
        var sm = new Http11ClientStateMachine(ops, new TurboClientOptions());
        sm.PreStart();
        sm.OnRequest(new HttpRequestMessage(HttpMethod.Get, "http://example.com/download") { Version = new Version(1, 1) });

        sm.DecodeServerData(Inbound(ChunkedResponse(chunks)));

        Assert.Single(ops.Responses);
        Assert.True(sm.ShouldPauseNetwork,
            "a full streamed-body receive queue must latch ShouldPauseNetwork (back-pressure)");
        return (sm, ops);
    }

    [Fact(Timeout = 5000)]
    public void Disposing_an_unread_streamed_response_does_not_release_receive_backpressure_LATENT_DEFECT()
    {
        var (sm, ops) = NewClientWithStreamingResponse(chunks: 64);

        // `using var response` — caller is done with the response but never read its body.
        ops.Responses[0].Dispose();

        // CURRENT (defective) behavior: still paused. QueuedBodyStream.Dispose is a no-op, so the reader
        // is never drained and the single H1.1 connection stays wedged. When the fix lands (dispose
        // drains/cancels the body), flip this to Assert.False.
        Assert.True(sm.ShouldPauseNetwork,
            "DEFECT: disposing an unread streamed response leaves the connection paused — it should release "
            + "back-pressure so a single H1.1 connection is not permanently stranded");
    }

    [Fact(Timeout = 5000)]
    public void Reading_the_streamed_body_releases_receive_backpressure()
    {
        var (sm, ops) = NewClientWithStreamingResponse(chunks: 64);

        // Draining the body via the consumer is the one path that DOES release the latch.
        var stream = ops.Responses[0].Content.ReadAsStream(TestContext.Current.CancellationToken);
        var buf = new byte[4];
        var guard = 0;
        while (sm.ShouldPauseNetwork && guard++ < 64)
        {
            _ = stream.Read(buf, 0, buf.Length);
        }

        Assert.False(sm.ShouldPauseNetwork,
            "reading the body must drain the queue below the back-pressure threshold and resume the network");
    }
}
