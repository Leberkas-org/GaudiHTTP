using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Streams.Stages.Features;

namespace TurboHttp.StreamTests.Concurrency;

/// <summary>
/// Regression tests for BidiFlow feedback-loop races.
/// <para>
/// BidiLoop-001/004 — <see cref="RedirectBidiStage"/>: verifies that <c>_inFlightCount</c>
/// stays balanced across redirect hops so that Case 2 completion fires reliably.
/// </para>
/// <para>
/// BidiLoop-002/003 — <see cref="RetryBidiStage"/>: verifies that retry storms are bounded
/// by <c>MaxRetries</c> and that the stage completes cleanly when In1 closes mid-retry.
/// </para>
/// </summary>
public sealed class BidiFlowFeedbackRaceTests : StreamTestBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Materialises a <see cref="RedirectBidiStage"/> with manual probes on both outlets
    /// and a manual publisher on the response inlet.
    /// </summary>
    private (TestSubscriber.ManualProbe<HttpRequestMessage> requestOut,
        TestSubscriber.ManualProbe<HttpResponseMessage> responseOut,
        Action<HttpResponseMessage> pushResponse,
        Action completeResponse) RunRedirect(
            RedirectPolicy policy,
            int requestOutDemand,
            int responseOutDemand,
            params HttpRequestMessage[] requests)
    {
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(new RedirectBidiStage(policy));
            var reqSrc = b.Add(Source.From(requests).Concat(Source.Never<HttpRequestMessage>()));
            var respSrc = b.Add(Source.FromPublisher(responsePublisher));

            b.From(reqSrc).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(respSrc).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var responseSub = responsePublisher.ExpectSubscription();
        var reqOutSub = requestOutProbe.ExpectSubscription();
        var respOutSub = responseOutProbe.ExpectSubscription();

        reqOutSub.Request(requestOutDemand);
        respOutSub.Request(responseOutDemand);

        return (requestOutProbe, responseOutProbe, responseSub.SendNext, responseSub.SendComplete);
    }

    /// <summary>
    /// Materialises a <see cref="RetryBidiStage"/> backed by <see cref="Source.Single"/> on
    /// the request side (In1 closes automatically after the one request).
    /// </summary>
    private (TestSubscriber.ManualProbe<HttpRequestMessage> requestOut,
        TestSubscriber.ManualProbe<HttpResponseMessage> responseOut,
        Action<HttpResponseMessage> pushResponse,
        Action completeResponse) RunRetry(
            RetryPolicy policy,
            HttpRequestMessage request,
            int requestOutDemand,
            int responseOutDemand)
    {
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(new RetryBidiStage(policy));
            // Source.Single closes In1 after the one element — this is the "closed mid-retry" scenario.
            var reqSrc = b.Add(Source.Single(request));
            var respSrc = b.Add(Source.FromPublisher(responsePublisher));

            b.From(reqSrc).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(respSrc).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var responseSub = responsePublisher.ExpectSubscription();
        var reqOutSub = requestOutProbe.ExpectSubscription();
        var respOutSub = responseOutProbe.ExpectSubscription();

        reqOutSub.Request(requestOutDemand);
        respOutSub.Request(responseOutDemand);

        return (requestOutProbe, responseOutProbe, responseSub.SendNext, responseSub.SendComplete);
    }

    private static HttpResponseMessage Redirect301(string location, HttpRequestMessage original)
    {
        var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently)
        {
            RequestMessage = original
        };
        response.Headers.Location = new Uri(location);
        return response;
    }

    private static HttpResponseMessage Response503(HttpRequestMessage original)
        => new(HttpStatusCode.ServiceUnavailable) { RequestMessage = original };

    private static HttpResponseMessage Response200(HttpRequestMessage? original = null)
        => new(HttpStatusCode.OK) { RequestMessage = original };

    // ─────────────────────────────────────────────────────────────────────────
    // BidiLoop-001 — 200 concurrent redirect instances all complete
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 30_000,
        DisplayName = "RFC-concurrency-BidiLoop-001: 200 concurrent redirect instances all complete")]
    public async Task ConcurrentRedirectInstances_AllComplete()
    {
        const int Count = 200;

        async Task RunOneAsync(int id)
        {
            var ct = CancellationToken.None;
            var policy = new RedirectPolicy();
            var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
            var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
            var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

            var originalRequest = new HttpRequestMessage(
                HttpMethod.Get, $"http://host{id}.example.com/");

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var bidi = b.Add(new RedirectBidiStage(policy));
                // Source.Single: In1 closes after the one request, so completion depends on _inFlightCount.
                var reqSrc = b.Add(Source.Single(originalRequest));
                var respSrc = b.Add(Source.FromPublisher(responsePublisher));

                b.From(reqSrc).To(bidi.Inlet1);
                b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
                b.From(respSrc).To(bidi.Inlet2);
                b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));

                return ClosedShape.Instance;
            })).Run(Materializer);

            var responseSub = await responsePublisher.ExpectSubscriptionAsync(ct);
            var reqOutSub = await requestOutProbe.ExpectSubscriptionAsync(ct);
            var respOutSub = await responseOutProbe.ExpectSubscriptionAsync(ct);

            // Request enough demand: original + redirect request on Out1; one final response on Out2.
            reqOutSub.Request(2);
            respOutSub.Request(1);

            // Stage forwards original request.
            var req1 = await requestOutProbe.ExpectNextAsync(ct);

            // Push a 301 redirect response; stage generates a new redirect request.
            responseSub.SendNext(Redirect301($"http://redirect{id}.example.com/", req1));

            // Stage emits the redirect request on Out1.
            var req2 = await requestOutProbe.ExpectNextAsync(ct);
            Assert.Equal($"http://redirect{id}.example.com/", req2.RequestUri!.OriginalString);

            // Push the final 200 response for the redirect request.
            responseSub.SendNext(Response200(req2));

            // Final response arrives at Out2.
            var finalResponse = await responseOutProbe.ExpectNextAsync(ct);
            Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);

            // Complete In2; Out2 and Out1 (Case 2: In1 already closed, _inFlightCount==0) complete.
            responseSub.SendComplete();
            await responseOutProbe.ExpectCompleteAsync(ct);
        }

        await Task.WhenAll(Enumerable.Range(0, Count).Select(RunOneAsync));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BidiLoop-002 — retry storm bounded by MaxRetries
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "RFC-concurrency-BidiLoop-002: retry storm bounded by MaxRetries")]
    public async Task RetryStorm_BoundedByMaxRetries()
    {
        // MaxRetries=2: attemptCount 1 → retry; attemptCount 2 → no retry (pass through).
        var policy = new RetryPolicy { MaxRetries = 2, RespectRetryAfter = false };
        var request = new HttpRequestMessage(HttpMethod.Get, "http://host.example.com/");
        var ct = CancellationToken.None;

        // Out1 will emit the request twice: original attempt + one retry.
        // Out2 will receive the second 503 (MaxRetries exceeded → pass-through).
        var (requestOut, responseOut, pushResponse, completeResponse) =
            RunRetry(policy, request, requestOutDemand: 2, responseOutDemand: 1);

        // Attempt 1: stage emits original request.
        var req1 = await requestOut.ExpectNextAsync(ct);

        // 503 → attemptCount=1 < MaxRetries=2 → retry.
        pushResponse(Response503(req1));

        // Stage emits the retry request on Out1.
        var req2 = await requestOut.ExpectNextAsync(ct);

        // 503 → attemptCount=2 >= MaxRetries=2 → no retry, pass through to Out2.
        pushResponse(Response503(req2));

        var finalResponse = await responseOut.ExpectNextAsync(ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, finalResponse.StatusCode);

        completeResponse();
        await responseOut.ExpectCompleteAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BidiLoop-003 — In1 closed during retry cycle; stage completes cleanly
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "RFC-concurrency-BidiLoop-003: In1 closed during retry cycle; stage completes cleanly")]
    public async Task In1ClosedDuringRetry_StageCompletesCleanly()
    {
        // RetryPolicy.Default (MaxRetries=3): first 503 triggers a retry.
        // Source.Single closes In1 immediately after the one request.
        var request = new HttpRequestMessage(HttpMethod.Get, "http://host.example.com/");
        var ct = CancellationToken.None;

        // Out1 expects 2 requests (original + retry); Out2 expects 1 final response.
        var (requestOut, responseOut, pushResponse, completeResponse) =
            RunRetry(RetryPolicy.Default, request, requestOutDemand: 2, responseOutDemand: 1);

        // Stage emits original request. (In1 is now closed — Source.Single completed.)
        var req1 = await requestOut.ExpectNextAsync(ct);

        // 503 → retry. Stage must NOT complete prematurely because a retry is pending.
        pushResponse(Response503(req1));

        // Stage emits the retry request despite In1 being closed.
        var req2 = await requestOut.ExpectNextAsync(ct);

        // Push a final 200 → passes through to Out2.
        pushResponse(Response200(req2));

        var finalResponse = await responseOut.ExpectNextAsync(ct);
        Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);

        // Complete In2; stage should now complete (In1 already closed, _inFlightCount==0).
        completeResponse();
        await responseOut.ExpectCompleteAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BidiLoop-004 — redirect + slow downstream liveness
    // ─────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "RFC-concurrency-BidiLoop-004: redirect with slow Out2 downstream — stage stays live")]
    public async Task SlowDownstream_StageStaysLive_AfterRedirect()
    {
        // Out2 receives exactly 1 unit of demand at startup — just enough to pull In2.
        // Redirect responses are consumed internally (never pushed to Out2), so the
        // 1 unit of Out2 demand survives the redirect hop and is available for the
        // final response. This verifies that Out1 stays live (redirect request emitted)
        // without any additional Out2 demand being added mid-flight.
        var ct = CancellationToken.None;
        var originalRequest = new HttpRequestMessage(HttpMethod.Get, "http://host.example.com/");
        var policy = new RedirectPolicy();

        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(new RedirectBidiStage(policy));
            var reqSrc = b.Add(
                Source.Single(originalRequest).Concat(Source.Never<HttpRequestMessage>()));
            var respSrc = b.Add(Source.FromPublisher(responsePublisher));

            b.From(reqSrc).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(respSrc).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var responseSub = await responsePublisher.ExpectSubscriptionAsync(ct);
        var reqOutSub = await requestOutProbe.ExpectSubscriptionAsync(ct);
        var respOutSub = await responseOutProbe.ExpectSubscriptionAsync(ct);

        // Out2 gets exactly 1 demand unit up front — the minimum needed to pull In2.
        // Out1 starts with 1 unit; we add a second unit only after receiving req1 to
        // ensure onPull(Out1) fires (setting _requestDemand=true) before the redirect
        // response arrives, avoiding a timing race on the Akka dispatcher.
        reqOutSub.Request(1);
        respOutSub.Request(1);

        // Stage emits original request on Out1.
        var req1 = await requestOutProbe.ExpectNextAsync(ct);

        // Request one more Out1 demand unit now — this fires onPull(Out1) synchronously
        // on the dispatcher, guaranteeing _requestDemand=true before SendNext below.
        // No additional Out2 demand is added; the 1 unit from above is still live.
        reqOutSub.Request(1);

        // Push redirect 301 response. The stage processes it internally:
        //   • enqueues redirect request
        //   • emits redirect request on Out1  (liveness: Out1 unblocked without extra Out2 demand)
        //   • does NOT push to Out2 (redirect response is consumed)
        //   • Out2 demand remains at 1 (unchanged)
        responseSub.SendNext(Redirect301("http://final.example.com/", req1));

        // Redirect request appears on Out1 despite no extra Out2 demand being added.
        var req2 = await requestOutProbe.ExpectNextAsync(ct);
        Assert.Equal("http://final.example.com/", req2.RequestUri!.OriginalString);

        // Push the final 200 response. Out2 still has its 1 demand unit → delivers it.
        responseSub.SendNext(Response200(req2));
        var finalResponse = await responseOutProbe.ExpectNextAsync(ct);
        Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);

        responseSub.SendComplete();
        await responseOutProbe.ExpectCompleteAsync(ct);
    }
}
