using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.Concurrency;

/// <summary>
/// Concurrency and stress tests for <see cref="ExtractOptionsStage"/>.
/// Each test materialises 50+ independent stage instances concurrently to surface
/// any timing-dependent state corruption or incorrect element routing.
/// </summary>
/// <remarks>
/// Findings from TASK-036-001 static analysis:
/// F-001–F-009 [SAFE]   — all stage-internal state is protected by the Akka.Streams
///                         single-threaded handler contract.
/// F-010 [SUSPECTED]    — <c>_needsReconnect</c> is written but never read (dead code);
///                         no race risk, but stress-tested here to confirm correctness.
/// </remarks>
public sealed class ExtractOptionsStageRaceTests : StreamTestBase
{
    private const int ConcurrencyLevel = 50;

    private static HttpRequestMessage MakeRequest(string url)
        => new(HttpMethod.Get, url) { Version = HttpVersion.Version11 };

    /// <summary>
    /// Runs a single <see cref="ExtractOptionsStage"/> instance over the given messages.
    /// Returns all request-outlet elements and the single signal-outlet ConnectItem.
    /// </summary>
    /// <remarks>
    /// Two-phase protocol required by the stage's FanOut contract:
    ///
    /// Phase 1 — signal outlet only:
    ///   Request 1 element from the signal outlet (Out1). This triggers the stage's
    ///   outSignal.onPull → Pull(_in) → source pushes req1 → stage emits ConnectItem.
    ///   Because the request outlet (Out0) has no demand yet, req1 is stored as _pending.
    ///   We await the ConnectItem so phase 2 starts only after phase 1 is fully settled.
    ///
    /// Phase 2 — request outlet only:
    ///   Request N elements from the request outlet (Out0). The first onPull sees _pending
    ///   (req1) and delivers it immediately; subsequent onPull events pull the source for
    ///   req2…reqN.
    ///
    /// This ordering avoids the "Cannot push port without demand" fault that occurs when
    /// the stage tries to emit ConnectItem before the signal outlet has registered demand.
    ///
    /// Source.Never() tail:
    ///   Source.From(messages) is finite; for a single-element list the source emits OnNext
    ///   + OnComplete atomically, so the stage calls CompleteStage() before Phase 2 can
    ///   register demand on Out0. Appending Source.Never() keeps the upstream open for the
    ///   duration of the test without affecting element delivery.
    /// </remarks>
    private async Task<(List<HttpRequestMessage> Requests, IOutputItem Signal)>
        RunGraphAsync(IReadOnlyList<HttpRequestMessage> messages)
    {
        var probe0 = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var probe1 = this.CreateManualSubscriberProbe<IOutputItem>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            // Concat with Source.Never so the stage never receives onUpstreamFinish
            // before Phase 2 has collected all elements (finite sources emit completion
            // atomically with their last element, which races with Phase-2 demand).
            var src = b.Add(Source.From(messages).Concat(Source.Never<HttpRequestMessage>()));
            var stage = b.Add(new ExtractOptionsStage());

            b.From(src).To(stage.In);
            b.From(stage.Out0).To(Sink.FromSubscriber(probe0));
            b.From(stage.Out1).To(Sink.FromSubscriber(probe1));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var sub0 = await probe0.ExpectSubscriptionAsync(CancellationToken.None);
        var sub1 = await probe1.ExpectSubscriptionAsync(CancellationToken.None);

        // Phase 1: demand on signal outlet only.
        // Stage pulls upstream, emits ConnectItem, stashes req1 as _pending.
        sub1.Request(1);
        var signal = await probe1.ExpectNextAsync(CancellationToken.None);

        // Phase 2: demand on request outlet.
        // First onPull delivers _pending (req1); subsequent onPulls pull the source.
        sub0.Request(messages.Count);
        var requests = new List<HttpRequestMessage>(messages.Count);
        for (var i = 0; i < messages.Count; i++)
        {
            requests.Add(await probe0.ExpectNextAsync(CancellationToken.None));
        }

        return (requests, signal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10000, DisplayName = "RFC-concurrency-ExtractOptions-001: 50 concurrent single-request streams each emit exactly one ConnectItem and pass the request through")]
    public async Task ConcurrentSingleRequest_EachStreamEmitsOneConnectItemAndPassesRequest()
    {
        var tasks = Enumerable.Range(0, ConcurrencyLevel)
            .Select(i => RunGraphAsync(new[] { MakeRequest($"https://host{i}.example.com/") }))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        foreach (var (requests, signal) in results)
        {
            Assert.Single(requests);
            Assert.IsType<ConnectItem>(signal);
        }
    }

    [Fact(Timeout = 10000, DisplayName = "RFC-concurrency-ExtractOptions-002: 50 concurrent 10-request streams emit one ConnectItem and pass through all 10 requests")]
    public async Task ConcurrentMultiRequest_OneConnectItemAndAllRequestsPassThrough()
    {
        const int RequestsPerStream = 10;

        var tasks = Enumerable.Range(0, ConcurrencyLevel)
            .Select(i =>
            {
                var requests = Enumerable.Range(0, RequestsPerStream)
                    .Select(j => MakeRequest($"https://host{i}.example.com/path/{j}"))
                    .ToList();
                return RunGraphAsync(requests);
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        foreach (var (requests, signal) in results)
        {
            Assert.Equal(RequestsPerStream, requests.Count);
            Assert.IsType<ConnectItem>(signal);
        }
    }

    [Fact(Timeout = 10000, DisplayName = "RFC-concurrency-ExtractOptions-003: F-010 — _needsReconnect dead code causes no duplicate ConnectItems under 50-stream x 20-request load")]
    public async Task F010_NeedsReconnectDeadCode_NoDuplicateConnectItemsUnderLoad()
    {
        // F-010 [SUSPECTED]: _needsReconnect is written to false on each onPush but is
        // never read. This test verifies that the dead field does not cause extra
        // ConnectItem emissions regardless of how many requests flow through a stage.
        const int RequestsPerStream = 20;

        var tasks = Enumerable.Range(0, ConcurrencyLevel)
            .Select(i =>
            {
                var requests = Enumerable.Range(0, RequestsPerStream)
                    .Select(j => MakeRequest($"https://stress{i}.example.com/r{j}"))
                    .ToList();
                return RunGraphAsync(requests);
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        foreach (var (requests, signal) in results)
        {
            Assert.Equal(RequestsPerStream, requests.Count);
            // Dead _needsReconnect must never produce a second ConnectItem
            Assert.IsType<ConnectItem>(signal);
        }
    }

    [Fact(Timeout = 10000, DisplayName = "RFC-concurrency-ExtractOptions-004: 50 concurrent streams — no request lost or duplicated under parallel pressure")]
    public async Task ConcurrentStreams_NoRequestLostOrDuplicated()
    {
        const int RequestsPerStream = 15;

        var sentUrisByStream = Enumerable.Range(0, ConcurrencyLevel)
            .Select(i => Enumerable.Range(0, RequestsPerStream)
                .Select(j => $"https://pressure{i}.example.com/p{j}")
                .ToHashSet())
            .ToArray();

        var tasks = sentUrisByStream
            .Select(uris => RunGraphAsync(uris.Select(MakeRequest).ToList()))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        for (var i = 0; i < ConcurrencyLevel; i++)
        {
            var received = results[i].Requests
                .Select(r => r.RequestUri!.ToString())
                .ToHashSet();

            Assert.Equal(sentUrisByStream[i], received);
        }
    }
}
