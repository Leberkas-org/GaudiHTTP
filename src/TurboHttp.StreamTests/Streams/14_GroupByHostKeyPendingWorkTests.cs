using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Tests that <see cref="GroupByHostKeyStage{T}"/> respects pending-work signals from
/// <see cref="IPendingWorkTracker"/> before completing substreams, preventing premature
/// completion while feature BidiStages (Retry, Cache) have in-flight re-injections.
/// </summary>
public sealed class GroupByHostKeyPendingWorkTests : StreamTestBase
{
    private static HttpRequestMessage Req(string url)
        => new(HttpMethod.Get, url);

    [Fact(Timeout = 10_000,
        DisplayName = "GBHPW-001: Substream completion is delayed while pending work exists")]
    public async Task Should_DelayCompletion_When_PendingWorkExists()
    {
        // Arrange: create a tracker with pending work that clears after a short delay.
        var tracker = new PendingWorkTracker();
        tracker.IncrementPending();

        // Clear pending work after 30ms — well within the 50ms retry window (5 × 10ms).
        _ = Task.Delay(30).ContinueWith(_ => tracker.DecrementPending());

        var requests = new[]
        {
            Req("http://example.com/1"),
            Req("http://example.com/2"),
        };

        var flow = (Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByHostKey(RequestEndpoint.FromRequest, maxSubstreams: 16, pendingWorkTracker: tracker)
                .MergeSubstreams();

        // Act: run pipeline — upstream completes immediately, but substream completion
        // should be delayed until tracker.IsPending becomes false.
        var results = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        // Assert: all requests flowed through despite pending work delaying completion.
        Assert.Equal(2, results.Count);
        Assert.False(tracker.IsPending);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "GBHPW-002: Substream force-completes after max retries when pending work never clears")]
    public async Task Should_ForceComplete_When_PendingWorkNeverClears()
    {
        // Arrange: pending work that never clears — should force-complete after 5 retries (50ms).
        var tracker = new PendingWorkTracker();
        tracker.IncrementPending();

        var requests = new[]
        {
            Req("http://stuck.example.com/1"),
        };

        var flow = (Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByHostKey(RequestEndpoint.FromRequest, maxSubstreams: 16, pendingWorkTracker: tracker)
                .MergeSubstreams();

        // Act: should complete within ~50ms of upstream finishing (not hang forever).
        var results = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        // Assert: all elements still passed through.
        Assert.Single(results);
        // Pending work is still true — we force-completed.
        Assert.True(tracker.IsPending);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "GBHPW-003: Substream completes immediately when no pending work tracker is set")]
    public async Task Should_CompleteImmediately_When_NoPendingWorkTracker()
    {
        var requests = new[]
        {
            Req("http://example.com/1"),
            Req("http://example.com/2"),
            Req("http://example.com/3"),
        };

        // No tracker — original behavior, no delays.
        var flow = (Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByHostKey(RequestEndpoint.FromRequest, maxSubstreams: 16)
                .MergeSubstreams();

        var results = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        Assert.Equal(3, results.Count);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "GBHPW-004: Substream completes immediately when tracker has no pending work")]
    public async Task Should_CompleteImmediately_When_TrackerHasNoPendingWork()
    {
        var tracker = new PendingWorkTracker();
        // Tracker exists but has zero pending work.

        var requests = new[]
        {
            Req("http://example.com/1"),
        };

        var flow = (Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByHostKey(RequestEndpoint.FromRequest, maxSubstreams: 16, pendingWorkTracker: tracker)
                .MergeSubstreams();

        var results = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        Assert.Single(results);
        Assert.False(tracker.IsPending);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "GBHPW-005: Re-injections flow through while substream alive during pending work delay")]
    public async Task Should_AllowReinjections_When_SubstreamAliveAndPendingWorkDelaysCompletion()
    {
        // Simulate a feature stage re-injecting a request while pending work is active.
        // The key insight: if pending work exists and a new element arrives before the
        // retry timer fires, it should flow through normally.
        var tracker = new PendingWorkTracker();
        tracker.IncrementPending();

        // Use a source that emits elements with a delay to simulate re-injection timing.
        // First two elements arrive immediately, third arrives after 20ms (while pending work active).
        var source = Source.From(new[]
            {
                Req("http://reinjection.example.com/original"),
                Req("http://reinjection.example.com/retry"),
            })
            .Concat(Source.Single(Req("http://reinjection.example.com/reinjected"))
                .InitialDelay(TimeSpan.FromMilliseconds(20)));

        // Clear pending work after all elements have been pushed.
        _ = Task.Delay(40).ContinueWith(_ => tracker.DecrementPending());

        var flow = (Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByHostKey(RequestEndpoint.FromRequest, maxSubstreams: 16, pendingWorkTracker: tracker)
                .MergeSubstreams();

        var results = await source
            .Via(flow)
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        // All three elements (including the "re-injected" one) should have passed through.
        Assert.Equal(3, results.Count);
        Assert.False(tracker.IsPending);
    }

    [Fact(Timeout = 10_000,
        DisplayName = "GBHPW-006: Multiple hosts complete independently with pending work tracker")]
    public async Task Should_CompleteIndependently_When_MultipleHostsWithPendingWork()
    {
        var tracker = new PendingWorkTracker();
        tracker.IncrementPending();

        // Clear after short delay.
        _ = Task.Delay(25).ContinueWith(_ => tracker.DecrementPending());

        var requests = new[]
        {
            Req("http://host-a.example.com/1"),
            Req("http://host-b.example.com/1"),
            Req("http://host-a.example.com/2"),
        };

        var flow = (Flow<HttpRequestMessage, HttpRequestMessage, NotUsed>)
            Flow.Create<HttpRequestMessage>()
                .GroupByHostKey(RequestEndpoint.FromRequest, maxSubstreams: 16, pendingWorkTracker: tracker)
                .MergeSubstreams();

        var results = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpRequestMessage>(), Materializer);

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.RequestUri!.Host == "host-a.example.com");
        Assert.Contains(results, r => r.RequestUri!.Host == "host-b.example.com");
    }
}
