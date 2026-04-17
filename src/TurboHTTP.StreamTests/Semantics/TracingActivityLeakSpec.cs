using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Diagnostics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;
using DiagnosticActivity = System.Diagnostics.Activity;
using ActivityCreationOptions = System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext>;
using ActivityListener = System.Diagnostics.ActivityListener;
using ActivitySamplingResult = System.Diagnostics.ActivitySamplingResult;
using ActivitySource = System.Diagnostics.ActivitySource;

namespace TurboHTTP.StreamTests.Semantics;

/// <summary>
/// Verifies that TracingBidiStage stops in-flight Activity spans on stage teardown.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="TracingBidiStage"/>.
/// Bug: No <c>PostStop</c> override — when the stage tears down with an in-flight request
/// (request pushed but no response received), <c>_currentActivity</c> is never stopped,
/// leaking an open span.
/// </remarks>
public sealed class TracingActivityLeakSpec : StreamTestBase
{
    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-9.1")]
    public async Task TracingBidiStage_should_stop_activity_when_stage_tears_down_without_response()
    {
        DiagnosticActivity? capturedActivity = null;
        var activityStopped = false;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TurboHttpInstrumentation.SourceName,
            Sample = (ref ActivityCreationOptions _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => capturedActivity = activity,
            ActivityStopped = _ => activityStopped = true
        };
        ActivitySource.AddActivityListener(listener);

        var stage = new TracingBidiStage();

        var reqPub = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var respPub = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var reqOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var respOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            b.From(Source.FromPublisher(reqPub)).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(reqOutProbe));
            b.From(Source.FromPublisher(respPub)).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(respOutProbe));
            return ClosedShape.Instance;
        })).Run(Materializer);

        var reqInSub = reqPub.ExpectSubscription(TestContext.Current.CancellationToken);
        var respInSub = respPub.ExpectSubscription(TestContext.Current.CancellationToken);
        var reqOutSub = reqOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var respOutSub = respOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        reqOutSub.Request(10);
        respOutSub.Request(10);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/tracing-leak");
        reqInSub.SendNext(request);
        reqOutProbe.ExpectNext(TestContext.Current.CancellationToken);

        Assert.NotNull(capturedActivity);
        Assert.False(activityStopped);

        // Tear down the stage completely — complete both inlets
        reqInSub.SendComplete();
        respInSub.SendComplete();

        // Cancel downstream to allow full stage shutdown
        reqOutSub.Cancel();
        respOutSub.Cancel();

        // Give the stage time to process teardown and invoke PostStop
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // After fix: PostStop stops the in-flight activity
        Assert.True(activityStopped);
    }
}
