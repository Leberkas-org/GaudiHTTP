using System.Net;
using System.Threading.Channels;
using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Lifecycle;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Lifecycle;

/// <summary>
/// When a response arrives for a request whose PendingRequest has already been completed or
/// cancelled (version mismatch → TrySetResult returns false), the orphaned HttpResponseMessage
/// must be disposed to prevent resource leaks.
/// </summary>
public sealed class ConsumerStaleResponseSpec : StreamTestBase
{
    [Fact(Timeout = 5000)]
    public async Task Consumer_should_dispose_response_when_pending_version_has_advanced()
    {
        var consumerId = Guid.NewGuid();
        var requestChannel = Channel.CreateUnbounded<HttpRequestMessage>();
        var responseChannel = Channel.CreateUnbounded<HttpResponseMessage>();
        var responseInjectChannel = Channel.CreateUnbounded<HttpResponseMessage>();
        var optionsFactory = () => new TurboRequestOptions(
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
        pending.TrySetCanceled();

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

        Assert.True(trackable.WasDisposed, "Stale response should have been disposed when TrySetResult returned false");

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
