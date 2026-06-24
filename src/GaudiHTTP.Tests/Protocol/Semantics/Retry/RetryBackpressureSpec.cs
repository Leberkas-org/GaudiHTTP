using System.Net;
using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Streams.Stages.Features;

namespace GaudiHTTP.Tests.Protocol.Semantics.Retry;

/// <summary>
/// Parked (Retry-After) retries must not backpressure new-request intake: when many requests on a
/// shared pipeline get 503 + Retry-After and accumulate in the waiting set, unrelated new requests
/// must still be accepted. The waiting set is bounded separately so it cannot grow without limit.
/// Previously the MaxPendingRetries cap counted waiting retries against new-request intake, which
/// head-of-line blocked the shared MergeHub.
/// </summary>
public sealed class RetryBackpressureSpec
{
    private sealed class FakeOps : IFeatureStageOperations
    {
        public List<HttpRequestMessage> PushedRequests { get; } = [];
        public List<HttpResponseMessage> PushedResponses { get; } = [];
        public List<string> ScheduledTimers { get; } = [];

        public void OnPushRequest(HttpRequestMessage request) => PushedRequests.Add(request);
        public void OnPushResponse(HttpResponseMessage response) => PushedResponses.Add(response);
        public void OnSignalPullRequest() { }
        public void OnSignalPullResponse() { }
        public void OnScheduleTimer(string key, TimeSpan delay) => ScheduledTimers.Add(key);
        public void OnCancelTimer(string key) => ScheduledTimers.Remove(key);
        public void OnCompleteStage() { }
        public Akka.Event.ILoggingAdapter Log => Akka.Event.NoLogger.Instance;
    }

    private static HttpResponseMessage Build503WithRetryAfter(HttpRequestMessage request)
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
        response.Headers.TryAddWithoutValidation("Retry-After", "10");
        return response;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2")]
    public void Parked_retries_should_not_block_new_request_intake()
    {
        var ops = new FakeOps();
        var sm = new RetryStateMachine(ops, new RetryPolicy { MaxRetries = 5 });

        for (var i = 0; i < RetryBidiStage.MaxPendingRetries; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/{i}");
            sm.OnRequest(request);
            sm.OnResponse(Build503WithRetryAfter(request));
        }

        Assert.Equal(RetryBidiStage.MaxPendingRetries, ops.ScheduledTimers.Count);
        // A full waiting set must NOT stop the stage from accepting new (unrelated) requests.
        Assert.True(sm.CanAcceptRequest);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-9.2")]
    public void Waiting_retries_should_be_bounded_independently()
    {
        var ops = new FakeOps();
        var sm = new RetryStateMachine(ops, new RetryPolicy { MaxRetries = 5 });

        for (var i = 0; i <= RetryBidiStage.MaxPendingRetries; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://example.com/{i}");
            sm.OnRequest(request);
            sm.OnResponse(Build503WithRetryAfter(request));
        }

        // Exactly MaxPendingRetries are parked; the over-cap response is forwarded, not queued.
        Assert.Equal(RetryBidiStage.MaxPendingRetries, ops.ScheduledTimers.Count);
        Assert.Single(ops.PushedResponses);
    }
}
