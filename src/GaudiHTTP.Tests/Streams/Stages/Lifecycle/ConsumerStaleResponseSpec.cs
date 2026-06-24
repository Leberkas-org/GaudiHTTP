using System.Net;
using System.Threading.Channels;
using Akka;
using Akka.Streams.Dsl;
using GaudiHTTP.Client;
using GaudiHTTP.Internal;
using GaudiHTTP.Streams.Lifecycle;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Streams.Stages.Lifecycle;

/// <summary>
/// When a response arrives for a request whose PendingRequest has already been completed or
/// cancelled (version mismatch → TrySetResult returns false), the Consumer must NOT dispose the
/// response. The BroadcastHub delivers the same HttpResponseMessage object to all consumers —
/// disposing in one consumer would corrupt the body stream that another consumer is reading.
/// The orphaned response is GC-reclaimable.
/// </summary>
public sealed class ConsumerStaleResponseSpec : StreamTestBase
{
    [Fact(Timeout = 5000)]
    public async Task Consumer_should_not_dispose_stale_response_because_broadcast_hub_shares_objects()
    {
        var consumerId = Guid.NewGuid();
        var requestChannel = Channel.CreateUnbounded<HttpRequestMessage>();
        var responseChannel = Channel.CreateUnbounded<HttpResponseMessage>();
        var responseInjectChannel = Channel.CreateUnbounded<HttpResponseMessage>();
        var optionsFactory = () => new GaudiRequestOptions(
            BaseAddress: new Uri("https://test.example"),
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);

        var (mergeHubSink, broadcastHubSource) = CreateTestHubsWithManualResponses(responseInjectChannel.Reader);

        var actor = Sys.ActorOf(Consumer.Props(
            consumerId,
            requestChannel.Reader,
            optionsFactory,
            responseChannel.Writer,
            mergeHubSink,
            broadcastHubSource,
            Materializer));

        // Set up a request with a PendingRequest + version
        var pending = PendingRequest.Rent();
        var staleVersion = pending.Version;

        var request = new HttpRequestMessage(HttpMethod.Get, "https://test.example/test");
        request.Options.Set(OptionsKey.Key, pending);
        request.Options.Set(OptionsKey.VersionKey, staleVersion);

        await requestChannel.Writer.WriteAsync(request, TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Advance the version by cancelling — now TrySetResult with the old version returns false
#pragma warning disable xUnit1051 // SUT behavior: simulates request cancellation, not test cooperative cancellation
        pending.TrySetCanceled();
#pragma warning restore xUnit1051

        // Inject a stale response that references the request (with old version in Options)
        var trackable = new TrackableContent();
        var staleResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = trackable,
        };
        await responseInjectChannel.Writer.WriteAsync(staleResponse, TestContext.Current.CancellationToken);

        // Give the sink time to process
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.False(trackable.WasDisposed,
            "Stale response must NOT be disposed — BroadcastHub shares the same object across consumers");

        Sys.Stop(actor);
    }

    private (Sink<HttpRequestMessage, NotUsed>, Source<HttpResponseMessage, NotUsed>) CreateTestHubsWithManualResponses(
        ChannelReader<HttpResponseMessage> responseReader)
    {
        var responseSource = ChannelSource.FromReader(responseReader);

        var mergeSink = MergeHub.Source<HttpRequestMessage>(16)
            .To(Sink.Ignore<HttpRequestMessage>())
            .Run(Materializer);

        return (mergeSink, responseSource);
    }

    private sealed class TrackableContent : HttpContent
    {
        public bool WasDisposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
