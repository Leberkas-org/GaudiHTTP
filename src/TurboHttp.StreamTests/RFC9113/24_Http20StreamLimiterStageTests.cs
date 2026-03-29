using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.RFC9113;

/// <summary>
/// Tests MAX_CONCURRENT_STREAMS enforcement in Http20StreamLimiterStage per RFC 9113 §5.1.2.
/// Verifies request queueing, dequeuing on stream close, queue overflow rejection,
/// timeout handling, and mid-connection limit updates.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http20StreamLimiterStage"/>.
/// RFC 9113 §5.1.2: An endpoint MUST NOT exceed the limit set by its peer for concurrent streams.
/// </remarks>
public sealed class Http20StreamLimiterStageTests : StreamTestBase
{
    private static HttpRequestMessage MakeRequest(string path = "/test")
        => new(HttpMethod.Get, $"https://example.com{path}");

    /// <summary>
    /// Creates a materialized stream with the limiter stage. Returns the handle, source queue, and sink probe.
    /// </summary>
    private (StreamLimiterHandle Handle, ISourceQueueWithComplete<HttpRequestMessage> Source, TestSubscriber.ManualProbe<HttpRequestMessage> Sink)
        CreateLimiterProbes(int maxConcurrentStreams, TimeSpan? queueTimeout = null, int maxPendingQueueSize = Http20StreamLimiterStage.DefaultMaxPendingQueueSize)
    {
        var handle = new StreamLimiterHandle();
        var sinkProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(
                Source.Queue<HttpRequestMessage>(256, OverflowStrategy.Backpressure),
                (b, src) =>
                {
                    var limiter = b.Add(new Http20StreamLimiterStage(handle, maxConcurrentStreams, queueTimeout, maxPendingQueueSize));

                    b.From(src).To(limiter.Inlet);
                    b.From(limiter.Outlet).To(Sink.FromSubscriber(sinkProbe));

                    return ClosedShape.Instance;
                }));

        var sourceQueue = graph.Run(Materializer);
        return (handle, sourceQueue, sinkProbe);
    }

    private static async Task OfferAsync(ISourceQueueWithComplete<HttpRequestMessage> queue, HttpRequestMessage request)
    {
        var result = await queue.OfferAsync(request).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsType<QueueOfferResult.Enqueued>(result);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-SL-001: Requests under limit pass through immediately")]
    public async Task Should_PassThrough_When_UnderConcurrentStreamsLimit()
    {
        var (handle, source, sink) = CreateLimiterProbes(3);

        var sub = sink.ExpectSubscription();
        sub.Request(10);

        // Send 2 requests (under limit of 3)
        await OfferAsync(source, MakeRequest("/1"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));

        await OfferAsync(source, MakeRequest("/2"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-SL-002: Request at limit is queued and not emitted")]
    public async Task Should_QueueRequest_When_AtConcurrentStreamsLimit()
    {
        var (handle, source, sink) = CreateLimiterProbes(2);

        var sub = sink.ExpectSubscription();
        sub.Request(10);

        // Fill to limit
        await OfferAsync(source, MakeRequest("/1"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));

        await OfferAsync(source, MakeRequest("/2"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));

        // 3rd request should be queued
        await OfferAsync(source, MakeRequest("/3"));
        sink.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-SL-003: Queued request released when stream closes")]
    public async Task Should_ReleaseQueuedRequest_When_StreamCloses()
    {
        var (handle, source, sink) = CreateLimiterProbes(2);

        var sub = sink.ExpectSubscription();
        sub.Request(10);

        // Fill to limit
        await OfferAsync(source, MakeRequest("/1"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));

        await OfferAsync(source, MakeRequest("/2"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));

        // Queue a 3rd request
        await OfferAsync(source, MakeRequest("/3"));
        sink.ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Close a stream → queued request should be released
        handle.OnStreamClosed!.Invoke();
        sink.ExpectNext(TimeSpan.FromSeconds(3));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-SL-004: Multiple queued requests released as streams close")]
    public async Task Should_ReleaseMultipleQueuedRequests_When_MultipleStreamsClose()
    {
        var (handle, source, sink) = CreateLimiterProbes(1);

        var sub = sink.ExpectSubscription();
        sub.Request(10);

        // Fill to limit (1 stream)
        await OfferAsync(source, MakeRequest("/1"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));

        // Queue 2 requests
        await OfferAsync(source, MakeRequest("/2"));
        sink.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        await OfferAsync(source, MakeRequest("/3"));

        // Close first stream → request 2 released
        handle.OnStreamClosed!.Invoke();
        sink.ExpectNext(TimeSpan.FromSeconds(3));

        // Close second stream → request 3 released
        handle.OnStreamClosed!.Invoke();
        sink.ExpectNext(TimeSpan.FromSeconds(3));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-SL-005: MAX_CONCURRENT_STREAMS=1 allows only one stream at a time")]
    public async Task Should_AllowOnlyOneStream_When_MaxConcurrentStreamsIs1()
    {
        var (handle, source, sink) = CreateLimiterProbes(1);

        var sub = sink.ExpectSubscription();
        sub.Request(10);

        // First request passes through
        await OfferAsync(source, MakeRequest("/1"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));

        // Second request is queued
        await OfferAsync(source, MakeRequest("/2"));
        sink.ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Close first → second released
        handle.OnStreamClosed!.Invoke();
        sink.ExpectNext(TimeSpan.FromSeconds(3));

        // Third request is queued again
        await OfferAsync(source, MakeRequest("/3"));
        sink.ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Close second → third released
        handle.OnStreamClosed!.Invoke();
        sink.ExpectNext(TimeSpan.FromSeconds(3));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-SL-006: Mid-connection MAX_CONCURRENT_STREAMS increase releases queued requests")]
    public async Task Should_ReleaseQueuedRequests_When_MaxConcurrentStreamsIncreased()
    {
        var (handle, source, sink) = CreateLimiterProbes(1);

        var sub = sink.ExpectSubscription();
        sub.Request(10);

        // Fill to limit
        await OfferAsync(source, MakeRequest("/1"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));

        // Queue a request
        await OfferAsync(source, MakeRequest("/2"));
        sink.ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Server increases limit to 3 → queued request should be released
        handle.OnMaxConcurrentStreamsChanged!.Invoke(3);
        sink.ExpectNext(TimeSpan.FromSeconds(3));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-SL-007: Mid-connection MAX_CONCURRENT_STREAMS decrease tightens limit")]
    public async Task Should_TightenLimit_When_MaxConcurrentStreamsDecreased()
    {
        var (handle, source, sink) = CreateLimiterProbes(5);

        var sub = sink.ExpectSubscription();
        sub.Request(10);

        // Open 3 streams
        await OfferAsync(source, MakeRequest("/1"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));
        await OfferAsync(source, MakeRequest("/2"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));
        await OfferAsync(source, MakeRequest("/3"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));

        // Server lowers limit to 3 → at capacity now
        handle.OnMaxConcurrentStreamsChanged!.Invoke(3);

        // Next request should be queued
        await OfferAsync(source, MakeRequest("/4"));
        sink.ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Close a stream → queued request released
        handle.OnStreamClosed!.Invoke();
        sink.ExpectNext(TimeSpan.FromSeconds(3));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-SL-008: Request timeout while waiting in queue fails the stage")]
    public async Task Should_FailStage_When_QueuedRequestTimesOut()
    {
        // Use a very short timeout for testing
        var (handle, source, sink) = CreateLimiterProbes(1, queueTimeout: TimeSpan.FromMilliseconds(500));

        var sub = sink.ExpectSubscription();
        sub.Request(10);

        // Fill to limit
        await OfferAsync(source, MakeRequest("/1"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));

        // Queue a request
        await OfferAsync(source, MakeRequest("/2"));

        // Wait for timeout
        var error = sink.ExpectError();
        Assert.IsType<Http2Exception>(error);
        Assert.Contains("timed out", error.Message);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-SL-009: Queue overflow rejects with Connection limit exceeded")]
    public async Task Should_FailStage_When_QueueOverflows()
    {
        // Use small queue size (3) and ManualProbe source for deterministic control
        var handle = new StreamLimiterHandle();
        var srcProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var sinkProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        RunnableGraph.FromGraph(
            GraphDsl.Create(b =>
            {
                var limiter = b.Add(new Http20StreamLimiterStage(handle, 1, maxPendingQueueSize: 3));
                var src = b.Add(Source.FromPublisher(srcProbe));

                b.From(src).To(limiter.Inlet);
                b.From(limiter.Outlet).To(Sink.FromSubscriber(sinkProbe));

                return ClosedShape.Instance;
            })).Run(Materializer);

        var sinkSub = sinkProbe.ExpectSubscription();
        sinkSub.Request(10);
        var srcSub = srcProbe.ExpectSubscription();

        // Fill to stream limit — stage will pull, we push 1
        srcSub.SendNext(MakeRequest("/0"));
        sinkProbe.ExpectNext(TimeSpan.FromSeconds(3));

        // Stage pulled again (continue-pulling after enqueue), push items to fill queue
        srcSub.SendNext(MakeRequest("/1")); // queued (1 of 3)
        srcSub.SendNext(MakeRequest("/2")); // queued (2 of 3)
        srcSub.SendNext(MakeRequest("/3")); // queued (3 of 3) — queue full
        srcSub.SendNext(MakeRequest("/overflow")); // overflow → FailStage

        var error = sinkProbe.ExpectError();
        Assert.IsType<Http2Exception>(error);
        Assert.Contains("Connection limit exceeded", error.Message);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-SL-010: Default queue timeout is 30 seconds")]
    public void Should_HaveDefaultQueueTimeoutOf30Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), Http20StreamLimiterStage.DefaultQueueTimeout);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-SL-011: DefaultMaxPendingQueueSize constant is 100")]
    public void Should_HaveDefaultMaxPendingQueueSizeOf100()
    {
        Assert.Equal(100, Http20StreamLimiterStage.DefaultMaxPendingQueueSize);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-SL-012: Stream close with empty queue is safe")]
    public async Task Should_HandleStreamClose_When_QueueIsEmpty()
    {
        var (handle, source, sink) = CreateLimiterProbes(3);

        var sub = sink.ExpectSubscription();
        sub.Request(10);

        // Open 1 stream
        await OfferAsync(source, MakeRequest("/1"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));

        // Close it — no queued requests, should be safe
        handle.OnStreamClosed!.Invoke();

        // Open another — should still work
        await OfferAsync(source, MakeRequest("/2"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-SL-013: Unlimited streams (int.MaxValue) passes all requests through")]
    public async Task Should_PassAllThrough_When_MaxConcurrentStreamsIsUnlimited()
    {
        var (handle, source, sink) = CreateLimiterProbes(int.MaxValue);

        var sub = sink.ExpectSubscription();
        sub.Request(20);

        for (var i = 0; i < 10; i++)
        {
            await OfferAsync(source, MakeRequest($"/{i}"));
            sink.ExpectNext(TimeSpan.FromSeconds(3));
        }
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-SL-014: Queued requests error contains active and max counts")]
    public async Task Should_IncludeCountsInTimeoutError_When_QueuedRequestTimesOut()
    {
        var (handle, source, sink) = CreateLimiterProbes(2, queueTimeout: TimeSpan.FromMilliseconds(500));

        var sub = sink.ExpectSubscription();
        sub.Request(10);

        // Fill to limit
        await OfferAsync(source, MakeRequest("/1"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));
        await OfferAsync(source, MakeRequest("/2"));
        sink.ExpectNext(TimeSpan.FromSeconds(3));

        // Queue a request
        await OfferAsync(source, MakeRequest("/3"));

        // Wait for timeout
        var error = sink.ExpectError();
        var http2Error = Assert.IsType<Http2Exception>(error);
        Assert.Equal(Http2ErrorCode.RefusedStream, http2Error.ErrorCode);
        Assert.Contains("active=2", error.Message);
        Assert.Contains("max=2", error.Message);
    }

    [Fact(Timeout = 10_000, DisplayName = "RFC9113-5.1.2-SL-015: Overflow error contains pending count and error code")]
    public async Task Should_IncludeCountsInOverflowError_When_QueueFull()
    {
        // Use small queue size (3) and ManualProbe source for deterministic control
        var handle = new StreamLimiterHandle();
        var srcProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var sinkProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();

        RunnableGraph.FromGraph(
            GraphDsl.Create(b =>
            {
                var limiter = b.Add(new Http20StreamLimiterStage(handle, 1, maxPendingQueueSize: 3));
                var src = b.Add(Source.FromPublisher(srcProbe));

                b.From(src).To(limiter.Inlet);
                b.From(limiter.Outlet).To(Sink.FromSubscriber(sinkProbe));

                return ClosedShape.Instance;
            })).Run(Materializer);

        var sinkSub = sinkProbe.ExpectSubscription();
        sinkSub.Request(10);
        var srcSub = srcProbe.ExpectSubscription();

        // Fill to stream limit
        srcSub.SendNext(MakeRequest("/0"));
        sinkProbe.ExpectNext(TimeSpan.FromSeconds(3));

        // Fill the queue to capacity
        srcSub.SendNext(MakeRequest("/1"));
        srcSub.SendNext(MakeRequest("/2"));
        srcSub.SendNext(MakeRequest("/3"));
        srcSub.SendNext(MakeRequest("/overflow"));

        var error = sinkProbe.ExpectError();
        var http2Error = Assert.IsType<Http2Exception>(error);
        Assert.Equal(Http2ErrorCode.RefusedStream, http2Error.ErrorCode);
        Assert.Contains("3 requests pending", error.Message);
    }
}
