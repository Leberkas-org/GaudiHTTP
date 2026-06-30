using System.Net;
using System.Threading.Channels;
using Akka.Streams.Dsl;
using GaudiHTTP.Client;
using GaudiHTTP.Internal;
using GaudiHTTP.Streams.Lifecycle;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Streams.Stages.Lifecycle;

/// <summary>
/// Repro for the high-concurrency client collapse observed in the 2026-06-19 benchmark run
/// (GaudiClientSendAsyncConcurrentBenchmarks at ConcurrencyLevel 4096, HTTP/2 + HTTP/3 → "NA",
/// 1229 exceptions, then a 120s WaitAsync timeout).
///
/// Root cause, from the benchmark log stack trace:
///   System.ObjectDisposedException: Cannot access a disposed object.
///   Object name: 'System.Net.Http.HttpRequestMessage'.
///      at System.Net.Http.HttpRequestMessage.set_Version(Version value)
///      at RequestEnricher.Enrich(...)                         RequestEnricher.cs:40
///      at Consumer.&lt;MaterializeIngress&gt;b__0(...)              Consumer.cs:97
///      at Select`2.Logic.OnPush()
///      at MergeHub`1.HubSink.SinkLogic.OnUpstreamFailure(Exception e)
///
/// Under load a request is cancelled (timeout) while still queued in the consumer's ingress
/// channel; the caller's `using` then disposes the HttpRequestMessage. The ingress later pulls
/// the now-disposed message through <see cref="RequestEnricher.Enrich"/>, whose
/// `request.Version = options.DefaultRequestVersion` throws ObjectDisposedException. Because the
/// enrichment runs as a bare <c>Select</c> feeding the SHARED <see cref="MergeHub"/>, that single
/// failure tears the consumer's producer off the hub ("removing from MergeHub now") and PERMANENTLY
/// kills request flow for the whole client — every other in-flight and future request is stranded.
///
/// This spec reproduces the defect deterministically at the stage level: one disposed request must
/// not strand sibling requests on the same consumer. It FAILS (sibling times out) until the ingress
/// enrichment is made failure-isolating (catch per element, complete that request's pending with the
/// error, drop it from the stream — never fail the Select / MergeHub producer).
/// </summary>
public sealed class ConsumerIngressFailureIsolationSpec : StreamTestBase
{
    // DefaultRequestVersion = 2.0 is what makes RequestEnricher Rule 2 execute `request.Version = ...`
    // (set_Version), the exact call that throws on the disposed message in the benchmark.
    private static GaudiRequestOptions Options() => new(
        BaseAddress: new Uri("https://test.example"),
        DefaultRequestHeaders: new HttpRequestMessage().Headers,
        DefaultRequestVersion: HttpVersion.Version20,
        DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
        Timeout: TimeSpan.FromSeconds(30),
        Credentials: null,
        PreAuthenticate: false);

    [Fact(Timeout = 15_000)]
    public async Task Consumer_ingress_should_isolate_a_disposed_request_and_keep_serving_siblings()
    {
        var ct = TestContext.Current.CancellationToken;
        var consumerId = Guid.NewGuid();
        var requestChannel = Channel.CreateUnbounded<HttpRequestMessage>();
        var responseChannel = Channel.CreateUnbounded<HttpResponseMessage>();

        var (mergeHubSink, broadcastHubSource) = CreateTestHubs();

        var actor = Sys.ActorOf(Consumer.Props(
            consumerId,
            requestChannel.Reader,
            Options,
            responseChannel.Writer,
            mergeHubSink,
            broadcastHubSource,
            Materializer));

        // 1) Baseline: a normal request flows end-to-end, proving the harness is healthy.
        var baseline = await RoundTripAsync(requestChannel.Writer, "https://test.example/baseline", ct);
        Assert.Equal(HttpStatusCode.OK, baseline.StatusCode);

        // 2) Poison: a request whose HttpRequestMessage has already been disposed by the caller
        //    (exactly what `using var request` does after a cancelled SendAsync). Enrich's
        //    `request.Version = 2.0` throws ObjectDisposedException inside the ingress Select.
        var poison = new HttpRequestMessage(HttpMethod.Get, "https://test.example/poison");
        poison.Dispose();
        await requestChannel.Writer.WriteAsync(poison, ct);

        // 3) Sibling: a perfectly valid request enqueued after the poison. It MUST still be served.
        //    With the bug, step 2 has already torn this consumer's producer off the shared MergeHub,
        //    so the sibling is never consumed and its pending never completes.
        var pending = PendingRequest.Rent();
        try
        {
            var responseTask = pending.GetValueTask();
            var sibling = new HttpRequestMessage(HttpMethod.Get, "https://test.example/sibling");
            sibling.Options.Set(OptionsKey.Key, pending);
            sibling.Options.Set(OptionsKey.VersionKey, pending.Version);
            await requestChannel.Writer.WriteAsync(sibling, ct);

            HttpResponseMessage siblingResponse;
            try
            {
                siblingResponse = await responseTask.AsTask().WaitAsync(TimeSpan.FromSeconds(3), ct);
            }
            catch (TimeoutException)
            {
                Assert.Fail(
                    "REPRO: a single disposed request failed the ingress Select and tore the consumer's " +
                    "producer off the shared MergeHub, stranding the sibling request. The per-request " +
                    "enrichment must be failure-isolated so one bad request never bricks the client.");
                return;
            }

            Assert.Equal(HttpStatusCode.OK, siblingResponse.StatusCode);
            Assert.Same(sibling, siblingResponse.RequestMessage);
        }
        finally
        {
            PendingRequest.Return(pending);
            Sys.Stop(actor);
        }
    }

    private async Task<HttpResponseMessage> RoundTripAsync(
        ChannelWriter<HttpRequestMessage> writer, string uri, CancellationToken ct)
    {
        var pending = PendingRequest.Rent();
        try
        {
            var responseTask = pending.GetValueTask();
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Options.Set(OptionsKey.Key, pending);
            request.Options.Set(OptionsKey.VersionKey, pending.Version);
            await writer.WriteAsync(request, ct);
            return await responseTask.AsTask().WaitAsync(TimeSpan.FromSeconds(3), ct);
        }
        finally
        {
            PendingRequest.Return(pending);
        }
    }

    // Mirrors ConsumerSpec.CreateTestHubs: a real MergeHub.Source (the shared client ingress)
    // mapping each enriched request to a 200 response, fanned out via a BroadcastHub.
    private (Sink<HttpRequestMessage, Akka.NotUsed>, Source<HttpResponseMessage, Akka.NotUsed>) CreateTestHubs()
    {
        var (sink, source) = MergeHub.Source<HttpRequestMessage>(16)
            .Via(Flow.Create<HttpRequestMessage>().Select(req =>
                new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req }))
            .ToMaterialized(BroadcastHub.Sink<HttpResponseMessage>(256), Keep.Both)
            .Run(Materializer);
        return (sink, source);
    }
}
