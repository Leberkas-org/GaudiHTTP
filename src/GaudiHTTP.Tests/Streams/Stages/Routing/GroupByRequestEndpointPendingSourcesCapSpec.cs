using System.Net;
using Akka;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Routing;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Routing;

/// <summary>
/// When downstream is slow consuming substream Sources but upstream keeps producing requests
/// to new endpoints, the stage's _pendingSources queue must be bounded. Without a cap, each new
/// endpoint creates a Source that piles up indefinitely — unbounded memory growth under load.
/// </summary>
public sealed class GroupByRequestEndpointPendingSourcesCapSpec : StreamTestBase
{
    [Fact(Timeout = 5000)]
    public async Task GroupBy_should_backpressure_upstream_when_pending_sources_accumulate()
    {
        int endpointSeq = 0;

        var stage = new GroupByRequestEndpointStage<HttpRequestMessage>(
            keyFor: _ => new RequestEndpoint
            {
                Host = $"host{Interlocked.Increment(ref endpointSeq)}",
                Port = 80,
                Scheme = "http",
                Version = HttpVersion.Version11,
            },
            maxSubstreams: -1,
            maxSubstreamsPerKey: _ => 1,
            maxConcurrencyPerSlot: _ => 1);

        var pulled = 0;
        const int total = 5000;

        // Downstream SinkProbe: never requests elements, so the outlet is never available.
        // Without a _pendingSources cap, HandlePush keeps re-pulling upstream because
        // per-slot Pending.Count is always ≤ 1 for brand-new endpoints — all 5000 get pulled
        // and 4999 Sources pile up in _pendingSources.
        var probe = Source.From(Enumerable.Range(0, total))
            .Select(_ =>
            {
                Interlocked.Increment(ref pulled);
                return new HttpRequestMessage(HttpMethod.Get, "http://x/");
            })
            .Via(Flow.FromGraph(stage))
            .RunWith(this.SinkProbe<Source<HttpRequestMessage, NotUsed>>(), Materializer);

        // Request 1 so the graph initializes, but no more — sources accumulate.
        probe.Request(1);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        var observed = Volatile.Read(ref pulled);
        // Must be bounded — with a _pendingSources cap, upstream should stop well below 5000.
        // Pre-fix: all 5000 get pulled because nothing gates upstream on pending source count.
        Assert.InRange(observed, 1, 999);
    }
}
