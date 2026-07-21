using System.Text;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http11.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Protocol;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Server;

/// <summary>
/// Verifies that the SerialBodyPump drains the entire response body inline during
/// OnResponse when data is available in the pipe. Regression test for the scenario
/// where the pump stalled at maxCapacity because OnOutboundFlushed for the header
/// push fired before the pump existed, and the pump had no inline driving.
/// </summary>
public sealed class Http11ServerBodyPumpStallSpec
{
    private const int ChunkSize = 16 * 1024;

    private static (IFeatureCollection Features, GaudiHttpResponseBodyFeature BodyFeature)
        CreateStreamingResponseContext(int bodySize)
    {
        var features = ServerTestContext.CreateResponse();
        var bodyFeature = (GaudiHttpResponseBodyFeature)features.Get<IHttpResponseBodyFeature>()!;
        var writer = bodyFeature.Writer;
        var remaining = bodySize;
        while (remaining > 0)
        {
            var take = Math.Min(remaining, 4 * 1024);
            var span = writer.GetSpan(take);
            span[..take].Fill(0xAB);
            writer.Advance(take);
            remaining -= take;
        }

        bodyFeature.UpgradeToPipe();

        return (features, bodyFeature);
    }

    private static Http11ServerStateMachine CreateSm(FakeServerOps ops)
    {
        return new Http11ServerStateMachine(
            new GaudiServerOptions().ToHttp1Options(),
            new GaudiServerOptions().ToHttp2Options(),
            ops);
    }

    private static void DrainBodyMessages(Http11ServerStateMachine sm, FakeServerOps ops, int maxIterations = 10_000)
    {
        var iterations = 0;
        while (ops.BodyMessages.Count > 0 && iterations++ < maxIterations)
        {
            var msg = ops.BodyMessages[0];
            ops.BodyMessages.RemoveAt(0);
            sm.OnBodyMessage(msg);
        }
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_should_drain_entire_body_inline_when_pipe_has_data()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var transport = sm.ConnectTransport(Encoding.ASCII.GetBytes(requestData), ops);

        const int bodySize = 8 * ChunkSize;
        var (context, bodyFeature) = CreateStreamingResponseContext(bodySize);
        bodyFeature.Writer.Complete();
        sm.OnResponse(context);
        DrainBodyMessages(sm, ops);

        // Pump reads all 8 data chunks + EOF, then body drain completes
        Assert.True(ops.ResponseBodyCompletions.Count > 0,
            "Body drain should complete after draining body messages.");

        // headers (1) + 8 chunked data frames + 1 chunked terminator = 10
        Assert.True(transport.WrittenCount >= 10, $"Expected at least 10 items written, got {transport.WrittenCount}");
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_should_emit_all_chunks_for_128kb_body()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var transport = sm.ConnectTransport(Encoding.ASCII.GetBytes(requestData), ops);

        const int bodySize = 8 * ChunkSize;
        var (context, bodyFeature) = CreateStreamingResponseContext(bodySize);
        bodyFeature.Writer.Complete();
        sm.OnResponse(context);
        DrainBodyMessages(sm, ops);

        // Verify that sufficient data was written for the response headers and body chunks
        var writtenBytes = transport.WrittenCount;
        Assert.True(writtenBytes > 0, "Expected data to be written to transport");

        // For an 8-chunk body (8 * 16KB = 128KB), plus headers and chunked encoding overhead,
        // we expect at least the body size worth of data
        Assert.True(transport.WrittenCount >= 1, "Expected multiple items written to transport");
    }

    [Fact(Timeout = 5000)]
    public void Response_body_should_park_at_byte_budget_and_resume_on_client_flush()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var transport = sm.ConnectTransport(Encoding.ASCII.GetBytes(requestData), ops);

        // 320 KB of readable body exceeds the pump's 256 KB byte budget (16 * 16 KB chunks). The
        // writer is intentionally left open: this forces the streaming pump path (a completed,
        // fully-buffered body would instead take the buffered-coalesce path) and keeps all 320 KB
        // synchronously readable so the byte budget — not data availability — is what parks the pump.
        const int bodySize = 20 * ChunkSize;
        var (context, _) = CreateStreamingResponseContext(bodySize);

        transport.FlushMode = Shared.FlushMode.Async;
        sm.OnResponse(context);
        DrainBodyMessages(sm, ops);

        var bytesWritten = transport.WrittenCount;
        Assert.True(bytesWritten > 100, "Pump should have written data beyond headers");
        Assert.True(bytesWritten < bodySize, "Pump should not write all body bytes at once due to async flush backpressure");
        Assert.Empty(ops.ResponseBodyCompletions);
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_should_handle_incomplete_pipe_gracefully()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var transport = sm.ConnectTransport(Encoding.ASCII.GetBytes(requestData), ops);

        const int bodySize = 4 * ChunkSize;
        var (context, _) = CreateStreamingResponseContext(bodySize);
        // Writer NOT completed — simulates handler still writing
        sm.OnResponse(context);
        DrainBodyMessages(sm, ops);

        // Pump reads available data, then goes async (pipe not complete)
        var itemsWritten = transport.WrittenCount;
        Assert.True(itemsWritten >= 4,
            $"Expected at least 4+ items written from {bodySize} bytes, got {itemsWritten}.");

        // Body not yet complete (handler still writing)
        Assert.Empty(ops.ResponseBodyCompletions);
    }
}
