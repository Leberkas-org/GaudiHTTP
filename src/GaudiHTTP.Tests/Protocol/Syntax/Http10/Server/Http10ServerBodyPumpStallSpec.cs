using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Syntax.Http10.Server;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http10.Server;

/// <summary>
/// Verifies that the H1.0 server handles the streaming response body path correctly,
/// especially the missing-Content-Length case that triggers connection-close framing.
/// </summary>
public sealed class Http10ServerBodyPumpStallSpec
{
    private const int ChunkSize = 16 * 1024;

    private static (IFeatureCollection Features, TurboHttpResponseBodyFeature BodyFeature)
        CreateStreamingResponseContext(int bodySize, bool setContentLength = false)
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        var responseFeature = new TurboHttpResponseFeature { StatusCode = 200 };
        if (setContentLength)
        {
            responseFeature.Headers["Content-Length"] = bodySize.ToString();
        }

        features.Set<IHttpResponseFeature>(responseFeature);

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

        // Force pipe creation so TryGetBufferedBody returns false when writer is NOT completed
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

    private static Http10ServerStateMachine CreateSm(FakeServerOps ops)
    {
        return new Http10ServerStateMachine(new TurboServerOptions().ToHttp1Options(), ops);
    }

    private static void SendRequest(Http10ServerStateMachine sm)
    {
        const string requestData = "GET / HTTP/1.0\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        sm.DecodeClientData(TransportData.Rent(MakeBuffer(requestData)));
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_should_emit_all_available_pipe_data_inline()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        const int bodySize = 4 * ChunkSize;
        // Writer NOT completed → streaming path, but data is in the pipe
        var (context, _) = CreateStreamingResponseContext(bodySize, setContentLength: true);
        sm.OnResponse(context);

        // Pump reads all available pipe data inline via inline driving
        var bodyItems = ops.Outbound.Skip(1).OfType<TransportData>().ToList();
        Assert.True(bodyItems.Count >= 4,
            $"Expected at least 4 body chunks from {bodySize} bytes, got {bodyItems.Count}.");
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_without_content_length_should_suppress_content_length_header()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        const int bodySize = 2 * ChunkSize;
        // Writer NOT completed + no Content-Length → streaming path with connection-close
        var (context, _) = CreateStreamingResponseContext(bodySize, setContentLength: false);
        sm.OnResponse(context);

        // Headers emitted without Content-Length (connection-close framing)
        var headerData = ops.Outbound.OfType<TransportData>().First();
        var headerText = Encoding.ASCII.GetString(headerData.Buffer.Span);
        Assert.DoesNotContain("Content-Length", headerText);

        // ShouldComplete deferred until body drain finishes
        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_with_content_length_should_include_content_length_header()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        SendRequest(sm);

        const int bodySize = 2 * ChunkSize;
        var (context, _) = CreateStreamingResponseContext(bodySize, setContentLength: true);
        sm.OnResponse(context);

        var headerData = ops.Outbound.OfType<TransportData>().First();
        var headerText = Encoding.ASCII.GetString(headerData.Buffer.Span);
        Assert.Contains("Content-Length", headerText);
        Assert.False(sm.ShouldComplete);
    }
}
