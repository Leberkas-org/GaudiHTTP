using GaudiHTTP.Tests.TestSupport;
using GaudiHTTP.Client;
using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Servus.Akka.Transport;
using GaudiHTTP.Protocol.Syntax.Http2;
using GaudiHTTP.Streams.Stages.Client;
using GaudiHTTP.Tests.Shared;
using static GaudiHTTP.Tests.Protocol.Syntax.Http2.Stages.Http2ConnectionTestHelper;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Stages;

public sealed class Http2ConnectionBackpressureSpec : StreamTestBase
{
    private (
        ISourceQueueWithComplete<HttpRequestMessage> RequestQueue,
        TestPublisher.ManualProbe<ITransportInbound> ServerProbe,
        TestSubscriber.ManualProbe<ITransportOutbound> NetworkProbe,
        TestSubscriber.ManualProbe<HttpResponseMessage> AppOutProbe)
        CreateProbes(int maxConcurrentStreams)
    {
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkProbe = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var appOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        var graph = RunnableGraph.FromGraph(
            GraphDsl.Create(
                Source.Queue<HttpRequestMessage>(16, OverflowStrategy.Backpressure),
                (b, reqSrc) =>
                {
                    var stage = b.Add(new Http20ClientConnectionStage(new GaudiClientOptions
                    { Http2 = { MaxConcurrentStreams = maxConcurrentStreams } }));
                    var srvSrc = b.Add(Source.FromPublisher(serverProbe));

                    b.From(srvSrc).To(stage.InNetwork);
                    b.From(stage.OutResponse).To(Sink.FromSubscriber(appOutProbe));
                    b.From(reqSrc).To(stage.InRequest);
                    b.From(stage.OutNetwork).To(Sink.FromSubscriber(networkProbe));

                    return ClosedShape.Instance;
                }));

        var requestQueue = graph.Run(Materializer);

        return (requestQueue, serverProbe, networkProbe, appOutProbe);
    }

    private static async Task OfferAsync(ISourceQueueWithComplete<HttpRequestMessage> queue, HttpRequestMessage request)
    {
        var result = await queue.OfferAsync(request)
            .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.IsType<QueueOfferResult.Enqueued>(result);
    }

    /// <summary>
    /// Waits until the transport's WrittenCount exceeds a known baseline, indicating
    /// new frame data (e.g. HEADERS) has been emitted by the SM.
    /// </summary>
    private static async Task WaitForTransportWriteAsync(InMemoryTransport transport, int writtenBefore,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(3));
        while (transport.WrittenCount <= writtenBefore)
        {
            await Task.Delay(10, cts.Token);
        }
    }

    /// <summary>
    /// Fills N streams: offers N requests, connects the transport on the first request,
    /// and waits for each request to be encoded (HEADERS written to transport).
    /// Returns the transport used for the connection.
    /// </summary>
    private static async Task<InMemoryTransport> FillStreamsAsync(
        ISourceQueueWithComplete<HttpRequestMessage> queue,
        TestSubscriber.ManualProbe<ITransportOutbound> networkProbe,
        StreamTestKit.PublisherProbeSubscription<ITransportInbound> srvSub,
        int count)
    {
        var transport = new InMemoryTransport();

        for (var i = 0; i < count; i++)
        {
            var writtenBefore = transport.WrittenCount;
            await OfferAsync(queue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));

            if (i == 0)
            {
                // First request emits ConnectTransport on OutNetwork
                await networkProbe.ExpectNextAsync(TestContext.Current.CancellationToken);

                // Send TransportConnected with the pipe transport.
                // The SM writes the preface + encodes the first request to the transport.
                srvSub.SendNext(CreateTransportConnected(transport));
            }

            // Wait for the SM to write HEADERS (or preface+HEADERS for the first request)
            await WaitForTransportWriteAsync(transport, writtenBefore, TestContext.Current.CancellationToken);
        }

        return transport;
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task Http2ConnectionBackpressure_should_stop_pulling_when_at_max_concurrent_streams_limit()
    {
        var (requestQueue, serverProbe, networkProbe, appOutProbe) = CreateProbes(3);

        var appOutSub = await appOutProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var networkSub = await networkProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var srvSub = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        appOutSub.Request(100);
        networkSub.Request(100);

        var transport = await FillStreamsAsync(requestQueue, networkProbe, srvSub, 3);

        // Offer a 4th request — should be gated (max concurrent streams = 3)
        var writtenBefore = transport.WrittenCount;
        await OfferAsync(requestQueue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));

        // Verify nothing is written to transport for 300ms (request is blocked)
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        Assert.Equal(writtenBefore, transport.WrittenCount);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task Http2ConnectionBackpressure_should_decrement_and_resume_pull_when_end_stream_received()
    {
        var (requestQueue, serverProbe, networkProbe, appOutProbe) = CreateProbes(3);

        var appOutSub = await appOutProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var networkSub = await networkProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var srvSub = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        appOutSub.Request(100);
        networkSub.Request(100);

        var transport = await FillStreamsAsync(requestQueue, networkProbe, srvSub, 3);

        // Offer a 4th request — gated
        await OfferAsync(requestQueue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        // Close stream 1 via DATA+END_STREAM through the transport
        var writtenBefore = transport.WrittenCount;
        transport.Feed(SerializeFrames(new DataFrame(streamId: 1, data: Array.Empty<byte>(), endStream: true)));

        // After stream close: blocked request should be encoded (HEADERS written to transport)
        await WaitForTransportWriteAsync(transport, writtenBefore, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task Http2ConnectionBackpressure_should_decrement_and_resume_pull_when_rst_stream_received()
    {
        var (requestQueue, serverProbe, networkProbe, appOutProbe) = CreateProbes(3);

        var appOutSub = await appOutProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var networkSub = await networkProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var srvSub = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        appOutSub.Request(100);
        networkSub.Request(100);

        var transport = await FillStreamsAsync(requestQueue, networkProbe, srvSub, 3);

        // Offer a 4th request — gated
        await OfferAsync(requestQueue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        // Send RST_STREAM for stream 3 through the transport
        var writtenBefore = transport.WrittenCount;
        transport.Feed(SerializeFrames(new RstStreamFrame(streamId: 3, Http2ErrorCode.Cancel)));

        // After RST_STREAM: blocked request should be encoded
        await WaitForTransportWriteAsync(transport, writtenBefore, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-5.1.2")]
    public async Task
        Http2ConnectionBackpressure_should_enforce_new_concurrent_streams_limit_when_settings_updated_mid_session()
    {
        var (requestQueue, serverProbe, networkProbe, appOutProbe) = CreateProbes(100);

        var appOutSub = await appOutProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var networkSub = await networkProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var srvSub = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        appOutSub.Request(100);
        networkSub.Request(100);

        var transport = await FillStreamsAsync(requestQueue, networkProbe, srvSub, 2);

        // Send SETTINGS with MaxConcurrentStreams=2 through the transport
        transport.Feed(SerializeFrames(new SettingsFrame(
            [(SettingsParameter.MaxConcurrentStreams, 2u)])));

        // Wait for SM to process SETTINGS and emit SETTINGS ACK to transport
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        // The stage had an outstanding pull from when limit was 100.
        // That in-flight pull will be satisfied by the next offered element regardless of the new limit.
        var writtenBefore = transport.WrittenCount;
        await OfferAsync(requestQueue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));
        await WaitForTransportWriteAsync(transport, writtenBefore, TestContext.Current.CancellationToken);

        // Now at activeStreams=3 with limit=2 — offer a 4th, should be gated
        writtenBefore = transport.WrittenCount;
        await OfferAsync(requestQueue, new HttpRequestMessage(HttpMethod.Get, "http://example.com/"));
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        Assert.Equal(writtenBefore, transport.WrittenCount);

        // Close streams 1 and 3 to drop to activeStreams=1 < limit=2 -> pull resumes.
        writtenBefore = transport.WrittenCount;
        transport.Feed(SerializeFrames(new DataFrame(streamId: 1, data: Array.Empty<byte>(), endStream: true)));
        transport.Feed(SerializeFrames(new RstStreamFrame(streamId: 3, Http2ErrorCode.Cancel)));

        // After two stream closures, the gated request is released
        await WaitForTransportWriteAsync(transport, writtenBefore, TestContext.Current.CancellationToken);
    }
}
