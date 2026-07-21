using System.Text;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Protocol.Body;
using GaudiHTTP.Protocol.Syntax.Http10.Server;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http10.Server;

/// <summary>
/// Verifies that the H1.0 server handles the streaming response body path correctly,
/// especially the missing-Content-Length case that triggers connection-close framing.
/// </summary>
public sealed class Http10ServerBodyPumpStallSpec
{
    private const int ChunkSize = 16 * 1024;

    private static (IFeatureCollection Features, GaudiHttpResponseBodyFeature BodyFeature)
        CreateStreamingResponseContext(int bodySize, bool setContentLength = false)
    {
        var features = new GaudiFeatureCollection();
        features.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature());
        var responseFeature = new GaudiHttpResponseFeature { StatusCode = 200 };
        if (setContentLength)
        {
            responseFeature.Headers["Content-Length"] = bodySize.ToString();
        }

        features.Set<IHttpResponseFeature>(responseFeature);

        var bodyFeature = new GaudiHttpResponseBodyFeature();
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

    private static Http10ServerStateMachine CreateSm(FakeServerOps ops)
    {
        return new Http10ServerStateMachine(new GaudiServerOptions().ToHttp1Options(), ops);
    }

    private static InMemoryTransport SendRequest(Http10ServerStateMachine sm, FakeServerOps ops)
    {
        const string requestData = "GET / HTTP/1.0\r\nHost: localhost\r\nContent-Length: 0\r\n\r\n";
        var data = Encoding.ASCII.GetBytes(requestData);
        return sm.ConnectTransport(data, ops);
    }

    private static void DrainBodyMessages(Http10ServerStateMachine sm, FakeServerOps ops, int maxIterations = 10_000)
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
    public void OnResponse_should_emit_all_available_pipe_data_inline()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var transport = SendRequest(sm, ops);

        const int bodySize = 4 * ChunkSize;
        // Writer NOT completed → streaming path, but data is in the pipe
        var (context, _) = CreateStreamingResponseContext(bodySize, setContentLength: true);
        sm.OnResponse(context);
        DrainBodyMessages(sm, ops);

        // Pump reads all available pipe data, producing body chunks
        // Should have written headers + body data; verify we got substantial output
        Assert.True(transport.WrittenCount > bodySize,
            $"Expected at least {bodySize} bytes from body + headers, got {transport.WrittenCount}.");
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_without_content_length_should_suppress_content_length_header()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var transport = SendRequest(sm, ops);

        const int bodySize = 2 * ChunkSize;
        // Writer NOT completed + no Content-Length → streaming path with connection-close
        var (context, _) = CreateStreamingResponseContext(bodySize, setContentLength: false);
        sm.OnResponse(context);

        // Headers emitted without Content-Length (connection-close framing)
        var headerText = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.DoesNotContain("Content-Length", headerText);

        // ShouldComplete deferred until body drain finishes
        Assert.False(sm.ShouldComplete);
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_with_content_length_should_include_content_length_header()
    {
        var ops = new FakeServerOps();
        var sm = CreateSm(ops);
        var transport = SendRequest(sm, ops);

        const int bodySize = 2 * ChunkSize;
        var (context, _) = CreateStreamingResponseContext(bodySize, setContentLength: true);
        sm.OnResponse(context);

        var headerText = Encoding.ASCII.GetString(transport.WrittenSpan);
        Assert.Contains("Content-Length", headerText);
        Assert.False(sm.ShouldComplete);
    }
}
