using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Server;

/// <summary>
/// RFC 9112 §9.3.2: a server MAY process pipelined requests in parallel but MUST send the
/// corresponding responses in the same order the requests were received. TurboHTTP guarantees
/// this by dispatching pipelined HTTP/1.1 requests to the application handler strictly
/// one-at-a-time, so the shared (completion-ordered) ApplicationBridgeStage can never reorder
/// or corrupt responses on a single H1.1 connection.
/// </summary>
public sealed class Http11ServerConnectionStagePipeliningSpec : StreamTestBase
{
    private sealed class FakeApplication(Func<IFeatureCollection, Task> handler)
        : IHttpApplication<IFeatureCollection>
    {
        public IFeatureCollection CreateContext(IFeatureCollection contextFeatures) => contextFeatures;
        public Task ProcessRequestAsync(IFeatureCollection context) => handler(context);
        public void DisposeContext(IFeatureCollection context, Exception? exception) { }
    }

    private static TransportConnected Connected()
        => new(new ConnectionInfo(
            new IPEndPoint(IPAddress.Loopback, 80),
            new IPEndPoint(IPAddress.Loopback, 50000),
            TransportProtocol.Tcp));

    private static TransportData PipelinedRequests(params string[] paths)
    {
        var sb = new StringBuilder();
        foreach (var path in paths)
        {
            sb.Append("GET ").Append(path).Append(" HTTP/1.1\r\nHost: example.com\r\n\r\n");
        }

        var bytes = Encoding.ASCII.GetBytes(sb.ToString());
        var buffer = TransportBuffer.Rent(bytes.Length);
        bytes.CopyTo(buffer.FullMemory.Span);
        buffer.Length = bytes.Length;
        return TransportData.Rent(buffer);
    }

    [Fact(Timeout = 15000)]
    [Trait("RFC", "RFC9112-9.3.2")]
    public void Http11_pipelined_requests_are_dispatched_one_at_a_time()
    {
        var options = new TurboServerOptions();
        var gates = new ConcurrentDictionary<string, TaskCompletionSource>();
        var probe = CreateTestProbe();

        // Each handler invocation blocks on its own gate. If pipelined requests were dispatched
        // concurrently, the probe would observe all three paths before any gate is released.
        var app = new FakeApplication(features =>
        {
            var path = features.Get<IHttpRequestFeature>()!.Path;
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            gates[path] = tcs;
            features.Get<IHttpResponseFeature>()!.StatusCode = 204;
            probe.Ref.Tell(path, ActorRefs.NoSender);
            return tcs.Task;
        });

        // Production wires the bridge with unbounded parallelism and routes connections through the
        // negotiating engine; the connection stage — not the bridge — must enforce H1.1 ordering, so
        // the test reproduces that exact path (negotiating engine + int.MaxValue bridge).
        var bridge = Flow.FromGraph(new ApplicationBridgeStage<IFeatureCollection>(
            app, int.MaxValue, options.HandlerTimeout, options.HandlerGracePeriod));

        var joined = new NegotiatingServerEngine(options).CreateFlow().Join(bridge);

        var (netIn, netOut) = this.SourceProbe<ITransportInbound>()
            .Via(joined)
            .ToMaterialized(this.SinkProbe<ITransportOutbound>(), Keep.Both)
            .Run(Materializer);

        netOut.Request(100);
        netIn.SendNext(Connected(), TestContext.Current.CancellationToken);
        netIn.SendNext(PipelinedRequests("/p/1", "/p/2", "/p/3"), TestContext.Current.CancellationToken);

        var first = probe.ExpectMsg<string>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("/p/1", first);
        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        gates[first].SetResult();

        var second = probe.ExpectMsg<string>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("/p/2", second);
        probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        gates[second].SetResult();

        var third = probe.ExpectMsg<string>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("/p/3", third);
        gates[third].SetResult();
    }
}
