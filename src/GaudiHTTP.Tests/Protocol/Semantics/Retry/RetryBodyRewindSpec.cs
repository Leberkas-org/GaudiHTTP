using System.Net;
using System.Text;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Protocol.Semantics;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Semantics.Retry;

/// <summary>
/// RFC 9110 §9.2.2: a request whose body cannot be replayed must not be retried automatically.
/// The protocol encoders consume the request body once, so a streamed (non-rewindable) body would
/// be truncated/empty on retry. Buffered content (ByteArrayContent etc.) re-materializes and is
/// still eligible. Previously RetryBidiStage hardcoded bodyPartiallyConsumed:false, retrying any
/// idempotent request regardless of body replayability.
/// </summary>
public sealed class RetryBodyRewindSpec : StreamTestBase
{
    private (TestSubscriber.ManualProbe<HttpRequestMessage> reqOut,
        TestSubscriber.ManualProbe<HttpResponseMessage> respOut,
        Action<HttpResponseMessage> pushResponse) RunManual(
            RetryBidiStage stage, HttpRequestMessage request)
    {
        var responsePublisher = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var requestOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var responseOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            var reqSrc = b.Add(Source.From([request]).Concat(Source.Never<HttpRequestMessage>()));
            var respSrc = b.Add(Source.FromPublisher(responsePublisher));

            b.From(reqSrc).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(requestOutProbe));
            b.From(respSrc).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(responseOutProbe));

            return ClosedShape.Instance;
        })).Run(Materializer);

        var responseSub = responsePublisher.ExpectSubscription(TestContext.Current.CancellationToken);
        var reqOutSub = requestOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var respOutSub = responseOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        reqOutSub.Request(5);
        respOutSub.Request(5);

        return (requestOutProbe, responseOutProbe, responseSub.SendNext);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void RetryCore_should_not_retry_when_body_is_non_rewindable()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/")
        {
            Content = new StreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("payload"))),
        };
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp) = RunManual(stage, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
        pushResp(response);

        // Non-rewindable body → no retry; the response is forwarded downstream instead.
        Assert.Same(response, respOut.ExpectNext(TestContext.Current.CancellationToken));
        reqOut.ExpectNoMsg(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9110-9.2.2")]
    public void RetryCore_should_retry_when_body_is_buffered_and_rewindable()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/")
        {
            Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("payload")),
        };
        var stage = new RetryBidiStage(new RetryPolicy());
        var (reqOut, respOut, pushResp) = RunManual(stage, request);

        reqOut.ExpectNext(TestContext.Current.CancellationToken);

        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
        pushResp(response);

        // Buffered body re-materializes on each read → idempotent PUT is retried.
        var retry = reqOut.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal(HttpMethod.Put, retry.Method);
        respOut.ExpectNoMsg(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
    }
}
