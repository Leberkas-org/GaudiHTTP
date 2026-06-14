using System.Linq;
using System.Net;
using System.Threading;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Routing;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Routing;

/// <summary>
/// The routing stage must propagate backpressure when a slot's bounded channel fills: it should
/// stop pulling upstream rather than draining every request into the slot's unbounded Pending
/// queue. Previously HandlePush re-pulled unconditionally, so a slow/blocked downstream let Pending
/// grow without limit.
/// </summary>
public sealed class GroupByRequestEndpointBackpressureSpec : StreamTestBase
{
    [Fact(Timeout = 10_000)]
    public async Task GroupBy_should_backpressure_upstream_when_slot_channel_is_full()
    {
        var endpoint = new RequestEndpoint
        {
            Host = "x", Port = 80, Scheme = "http", Version = HttpVersion.Version11,
        };

        var stage = new GroupByRequestEndpointStage<HttpRequestMessage>(
            keyFor: _ => endpoint,
            maxSubstreams: 1,
            maxSubstreamsPerKey: _ => 1,
            maxConcurrencyPerSlot: _ => 1);

        var pulled = 0;
        const int total = 5000;

        // Emit the produced substream Source(s) but never run/consume them, so the single slot's
        // bounded channel fills and stays full. Fire-and-forget: with backpressure the stream never
        // completes (that is the point), so we sample the pulled count after a short settle.
        _ = Source.From(Enumerable.Range(0, total))
            .Select(_ =>
            {
                Interlocked.Increment(ref pulled);
                return new HttpRequestMessage(HttpMethod.Get, "http://x/");
            })
            .Via(Flow.FromGraph(stage))
            .RunForeach(_ => { }, Materializer);

        await Task.Delay(500, TestContext.Current.CancellationToken);

        var observed = Volatile.Read(ref pulled);
        // Must have started pulling (lower bound) but be bounded well below `total` (upper bound):
        // pre-fix the stage grabbed all 5000 into the unbounded Pending queue.
        Assert.InRange(observed, 1, 999);
    }
}
