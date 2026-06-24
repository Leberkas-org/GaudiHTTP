using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http11.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http11.Server;

/// <summary>
/// Verifies that the SerialBodyPump drains the entire response body inline during
/// OnResponse when data is available in the pipe. Regression test for the scenario
/// where the pump stalled at maxCapacity because OnOutboundFlushed for the header
/// push fired before the pump existed, and the pump had no inline driving.
/// </summary>
public sealed class Http11ServerBodyPumpStallSpec
{
    private const int ChunkSize = 16 * 1024;

    private static (IFeatureCollection Features, TurboHttpResponseBodyFeature BodyFeature)
        CreateStreamingResponseContext(int bodySize)
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });

        var bodyFeature = new TurboHttpResponseBodyFeature();
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

        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return (features, bodyFeature);
    }

    private static TransportBuffer MakeBuffer(string raw)
    {
        var data = Encoding.ASCII.GetBytes(raw);
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        return buffer;
    }

    private static Http11ServerStateMachine CreateSm(FakeServerOps ops)
    {
        return new Http11ServerStateMachine(
            new TurboServerOptions().ToHttp1Options(),
            new TurboServerOptions().ToHttp2Options(),
            ops);
    }

    private static void SendRequest(Http11ServerStateMachine sm)
    {
        const string requestData = "GET / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        sm.DecodeClientData(TransportData.Rent(MakeBuffer(requestData)));
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_should_drain_entire_body_inline_when_pipe_has_data()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        const int bodySize = 8 * ChunkSize;
        var (context, bodyFeature) = CreateStreamingResponseContext(bodySize);
        bodyFeature.Writer.Complete();
        sm.OnResponse(context);

        // Inline driving: pump reads all 8 data chunks + EOF during OnResponse
        Assert.True(ops.ResponseBodyCompletions.Count > 0,
            "Body drain should complete inline during OnResponse.");

        // headers (1) + 8 chunked data frames + 1 chunked terminator = 10
        Assert.Equal(10, ops.Outbound.Count);
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_should_emit_all_chunks_for_128kb_body()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        const int bodySize = 8 * ChunkSize;
        var (context, bodyFeature) = CreateStreamingResponseContext(bodySize);
        bodyFeature.Writer.Complete();
        sm.OnResponse(context);

        var bodyDataBytes = ops.Outbound
            .Skip(1)
            .OfType<TransportData>()
            .Where(td => td.Buffer.Length > 5)
            .Sum(td =>
            {
                var span = td.Buffer.Span;
                var headerEnd = span.IndexOf((byte)'\n') + 1;
                var trailerLen = 2;
                return td.Buffer.Length - headerEnd - trailerLen;
            });

        Assert.True(bodyDataBytes >= bodySize,
            $"Expected at least {bodySize} body bytes, got {bodyDataBytes}.");
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_should_handle_incomplete_pipe_gracefully()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        const int bodySize = 4 * ChunkSize;
        var (context, _) = CreateStreamingResponseContext(bodySize);
        // Writer NOT completed — simulates handler still writing
        sm.OnResponse(context);

        // Pump reads all available data inline, then goes async (pipe not complete)
        var bodyItems = ops.Outbound.Skip(1).OfType<TransportData>().ToList();
        Assert.True(bodyItems.Count >= 4,
            $"Expected at least 4 body chunks from {bodySize} bytes, got {bodyItems.Count}.");

        // Body not yet complete (handler still writing)
        Assert.Empty(ops.ResponseBodyCompletions);
    }
}
