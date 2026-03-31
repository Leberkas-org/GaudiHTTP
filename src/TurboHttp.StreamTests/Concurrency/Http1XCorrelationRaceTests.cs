using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.Streams.Stages.Routing;

namespace TurboHttp.StreamTests.Concurrency;

/// <summary>
/// Concurrency and stress tests for <see cref="Http1XCorrelationStage"/>.
/// Covers ordering and cancellation-equivalent races documented in TASK-036-002.
/// </summary>
/// <remarks>
/// Findings from TASK-036-002 static analysis (updated for TASK-038-002):
/// F-001–F-007 [SAFE]   — all internal state is protected by the Akka.Streams
///                         single-threaded handler contract; no external races possible.
/// F-008 [RESOLVED]     — with strict one-request-in-flight, at most one request is ever
///                         in the stage at a time; silent discard only occurs when the
///                         response stream closes after the first pair completes.
/// F-009 [CONFIRMED]    — upstream failure absorbed as graceful completion; errors hidden from downstream.
/// F-010 [REMOVED]      — InReset inlet removed; reconnect no longer touches queue state.
/// F-011 [RESOLVED]     — TryCorrelateAndEmit while-loop removed; strict serial push model.
/// </remarks>
public sealed class Http1XCorrelationRaceTests : StreamTestBase
{
    private const int ConcurrencyLevel = 50;

    private static HttpResponseMessage OkResponse() => new(HttpStatusCode.OK);

    private static HttpRequestMessage MakeRequest(string url) =>
        new(HttpMethod.Get, url) { Version = HttpVersion.Version11 };

    /// <summary>
    /// Builds and materialises a closed graph wrapping <see cref="Http1XCorrelationStage"/>.
    /// Returns collected responses once the stream completes.
    /// </summary>
    private async Task<List<HttpResponseMessage>> RunStageAsync(
        Source<HttpRequestMessage, NotUsed> requestSource,
        Source<HttpResponseMessage, NotUsed> responseSource,
        TimeSpan? timeout = null)
    {
        var sink = Sink.Seq<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(GraphDsl.Create(sink, (b, s) =>
        {
            var corr = b.Add(new Http1XCorrelationStage());
            var reqSrc = b.Add(requestSource);
            var resSrc = b.Add(responseSource);
            var signalSink = b.Add(Sink.Ignore<IControlItem>().MapMaterializedValue(_ => NotUsed.Instance));

            b.From(reqSrc).To(corr.InRequest);
            b.From(resSrc).To(corr.InResponse);
            b.From(corr.OutResponse).To(s);
            b.From(corr.OutControl).To(signalSink);

            return ClosedShape.Instance;
        }));

        var task = graph.Run(Materializer);
        return (await task.WaitAsync(timeout ?? TimeSpan.FromSeconds(8))).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Stress: 50 concurrent stage instances
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10000, DisplayName = "RFC-concurrency-H1XCorr-001: 50 concurrent stage instances — FIFO pairing correct under parallel load")]
    public async Task ConcurrentStreams_FifoPairingCorrectUnderLoad()
    {
        // Materialises ConcurrencyLevel independent stage instances simultaneously.
        // Each runs 10 request/response pairs and verifies that every response is
        // attributed to the correct request by FIFO position.
        const int PairsPerStream = 10;

        var tasks = Enumerable.Range(0, ConcurrencyLevel).Select(streamIdx =>
        {
            var requests = Enumerable.Range(0, PairsPerStream)
                .Select(i => MakeRequest($"https://host{streamIdx}.example.com/r{i}"))
                .ToArray();

            var responses = Enumerable.Range(0, PairsPerStream)
                .Select(_ => OkResponse())
                .ToArray();

            return RunStageAsync(Source.From(requests), Source.From(responses))
                .ContinueWith(t => (Requests: requests, Responses: t.Result));
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        foreach (var (requests, responses) in results)
        {
            Assert.Equal(PairsPerStream, responses.Count);
            for (var i = 0; i < PairsPerStream; i++)
            {
                Assert.Same(requests[i], responses[i].RequestMessage);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Acceptance criterion 1: serial requests — ordering behaviour
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10000, DisplayName = "RFC-concurrency-H1XCorr-002: Serial requests with delayed responses — FIFO pairing still correct")]
    public async Task SerialRequests_DelayedResponses_FifoPairingCorrect()
    {
        // Strict back-pressure: only req1 is pulled until res1 arrives.
        // Then req2, then req3. Responses arrive in the same order (RFC 9112 §9).
        var req1 = MakeRequest("https://example.com/1");
        var req2 = MakeRequest("https://example.com/2");
        var req3 = MakeRequest("https://example.com/3");

        var res1 = OkResponse();
        var res2 = OkResponse();
        var res3 = OkResponse();

        var results = await RunStageAsync(
            Source.From(new[] { req1, req2, req3 }),
            Source.From(new[] { res1, res2, res3 }).InitialDelay(TimeSpan.FromMilliseconds(200)));

        Assert.Equal(3, results.Count);
        Assert.Same(req1, results[0].RequestMessage);
        Assert.Same(req2, results[1].RequestMessage);
        Assert.Same(req3, results[2].RequestMessage);
    }

    [Fact(Timeout = 10000, DisplayName = "RFC-concurrency-H1XCorr-003: Out-of-order response delivery causes FIFO mismatch — silent, no stream error")]
    public async Task SerialRequests_OutOfOrderResponseDelivery_CausesSilentFifoMismatch_NoStreamError()
    {
        // HTTP/1.x responses carry no request identifier; the stage blindly pairs by
        // arrival position. If a buggy server sends responses out of order, the stage
        // silently attributes the wrong request to each response — no stream error.
        var req1 = MakeRequest("https://example.com/req1");
        var req2 = MakeRequest("https://example.com/req2");

        var resA = OkResponse(); // arrives first
        var resB = OkResponse(); // arrives second

        var results = await RunStageAsync(
            Source.From(new[] { req1, req2 }),
            Source.From(new[] { resA, resB }).InitialDelay(TimeSpan.FromMilliseconds(150)));

        Assert.Equal(2, results.Count);
        // FIFO pairing: req1 gets the first-arriving response, req2 gets the second.
        Assert.Same(req1, results[0].RequestMessage);
        Assert.Same(req2, results[1].RequestMessage);
        Assert.Same(resA, results[0]);
        Assert.Same(resB, results[1]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Acceptance criterion 2: upstream failure mid-correlation (F-009 [CONFIRMED])
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10000, DisplayName = "RFC-concurrency-H1XCorr-004: Response upstream failure mid-correlation — stream completes gracefully, no phantom queue entry (F-009)")]
    public async Task ResponseUpstreamFailure_MidCorrelation_StreamCompletesGracefully_NoPhantomEntry()
    {
        // F-009 [CONFIRMED]: upstream failures are absorbed as graceful completion.
        // Sequence: req1 → res1 paired, emitted; then response upstream fails.
        var req1 = MakeRequest("https://example.com/1");
        var req2 = MakeRequest("https://example.com/2");
        var req3 = MakeRequest("https://example.com/3");

        var res1 = OkResponse();

        var results = await RunStageAsync(
            Source.From(new[] { req1, req2, req3 }),
            Source.Single(res1)
                  .Concat(Source.Failed<HttpResponseMessage>(new IOException("TCP reset mid-stream"))));

        // Only res1 was emitted before the failure.
        Assert.Single(results);
        Assert.Same(req1, results[0].RequestMessage);
        Assert.Same(res1, results[0]);
    }

    [Fact(Timeout = 10000, DisplayName = "RFC-concurrency-H1XCorr-005: Request upstream failure mid-correlation — stream completes gracefully (F-009)")]
    public async Task RequestUpstreamFailure_MidCorrelation_StreamCompletesGracefully()
    {
        // F-009 [CONFIRMED]: request-side upstream failures are also absorbed.
        var failingRequestSource =
            Source.Single(MakeRequest("https://example.com/before-fail"))
                  .Concat(Source.Failed<HttpRequestMessage>(new InvalidOperationException("app disposed")));

        var results = await RunStageAsync(
            failingRequestSource,
            Source.Never<HttpResponseMessage>());

        Assert.Empty(results);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Acceptance criterion 3: connection reuse after error — clean state
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10000, DisplayName = "RFC-concurrency-H1XCorr-006: New stage instance after prior failure has clean correlation state (connection reuse)")]
    public async Task ConnectionReuseAfterError_NewStageInstanceHasCleanCorrelationState()
    {
        // Each materialisation of Http1XCorrelationStage creates a new Logic object
        // with no shared mutable state between materialisations.
        var failingRequest =
            Source.Single(MakeRequest("https://old-connection.example.com/"))
                  .Concat(Source.Failed<HttpRequestMessage>(new IOException("connection drop")));

        try
        {
            await RunStageAsync(failingRequest, Source.Never<HttpResponseMessage>());
        }
        catch
        {
            // Absorb; F-009 means this actually completes normally, but guard anyway.
        }

        var req = MakeRequest("https://new-connection.example.com/clean");
        var res = OkResponse();

        var results = await RunStageAsync(Source.Single(req), Source.Single(res));

        Assert.Single(results);
        Assert.Same(req, results[0].RequestMessage);
        Assert.Same(res, results[0]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // F-008 [RESOLVED]: Graceful handling when response stream closes early
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10000, DisplayName = "RFC-concurrency-H1XCorr-007: F-008 — response stream closes after first pair; stage completes when second request arrives with no response")]
    public async Task F008_ResponseStreamClosesEarly_StageCompletesGracefully()
    {
        // With strict one-request-in-flight: req1 is pulled, paired with res1.
        // InResponse then closes. req2 is pulled, but InResponse is already closed →
        // stage completes gracefully. req3 is never pulled.
        var req1 = MakeRequest("https://example.com/1");
        var req2 = MakeRequest("https://example.com/2");
        var req3 = MakeRequest("https://example.com/3");

        var results = await RunStageAsync(
            Source.From(new[] { req1, req2, req3 }),
            Source.Single(OkResponse()));

        // Only req1 was paired before InResponse closed.
        Assert.Single(results);
        Assert.Same(req1, results[0].RequestMessage);
    }

    [Fact(Timeout = 10000, DisplayName = "RFC-concurrency-H1XCorr-008: F-008 — downstream sees graceful completion (not error) even when request has no response")]
    public async Task F008_DownstreamSeesGracefulCompletion_WhenRequestHasNoResponse()
    {
        // Single request, zero responses. Stage completes gracefully when
        // InResponse (Source.Empty) closes while the request is in flight.
        var results = await RunStageAsync(
            Source.Single(MakeRequest("https://abandoned.example.com/")),
            Source.Empty<HttpResponseMessage>());

        Assert.Empty(results);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // F-011 [RESOLVED]: All N pairs emitted correctly in strict serial model
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10000, DisplayName = "RFC-concurrency-H1XCorr-009: All N pairs emitted correctly in strict serial one-request-at-a-time model")]
    public async Task StrictSerial_AllPairsEmittedCorrectly()
    {
        // Strict back-pressure: each request is pulled only after the previous response
        // is received. All N correlated pairs are emitted in FIFO order.
        const int N = 20;

        var requests = Enumerable.Range(0, N)
            .Select(i => MakeRequest($"https://example.com/r{i}"))
            .ToArray();

        var responses = Enumerable.Range(0, N)
            .Select(_ => OkResponse())
            .ToArray();

        // Delay responses so back-pressure is exercised between each pair.
        var results = await RunStageAsync(
            Source.From(requests),
            Source.From(responses).InitialDelay(TimeSpan.FromMilliseconds(100)));

        Assert.Equal(N, results.Count);
        for (var i = 0; i < N; i++)
        {
            Assert.Same(requests[i], results[i].RequestMessage);
        }
    }
}
