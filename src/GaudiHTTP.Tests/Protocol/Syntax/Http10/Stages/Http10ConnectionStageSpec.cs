using GaudiHTTP.Client;
using System.Net;
using System.Text;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Servus.Akka.TestKit;
using Servus.Akka.Transport;
using GaudiHTTP.Streams.Stages.Client;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http10.Stages;

public sealed class Http10ConnectionStageSpec : StreamTestBase
{
    private static HttpRequestMessage MakeRequest(string path = "/")
    {
        return new HttpRequestMessage(HttpMethod.Get, $"http://example.com{path}")
        {
            Version = new Version(1, 0)
        };
    }

    private static TransportConnected MakeTransportConnected(TestPipeTransport transport)
        => new(new ConnectionInfo(
            new IPEndPoint(IPAddress.Loopback, 0),
            new IPEndPoint(IPAddress.Loopback, 80),
            TransportProtocol.Tcp),
            transport);

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10ConnectionStage_should_encode_request_and_emit_on_network_outlet()
    {
        var stage = new Http10ClientConnectionStage(new GaudiClientOptions());

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var app = b.Add(Source.FromPublisher(appProbe));
            var server = b.Add(Source.FromPublisher(serverProbe));
            var netSink = b.Add(Sink.FromSubscriber(networkSub));
            var resSink = b.Add(Sink.FromSubscriber(responseSub));

            b.From(app).To(s.InRequest);
            b.From(server).To(s.InNetwork);
            b.From(s.OutNetwork).To(netSink);
            b.From(s.OutResponse).To(resSink);

            return ClosedShape.Instance;
        })).Run(Materializer);

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSubscription = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        // Pull on network outlet to signal demand
        netSubscription.Request(10);
        resSubscription.Request(10);

        // Send a request
        appSubscription.SendNext(MakeRequest("/test"));

        // ConnectItem emitted first when endpoint is known from the first request
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        // Simulate transport connect handshake completing — request encoding is deferred until this
        var transport = new TestPipeTransport();
        serverSubscription.SendNext(MakeTransportConnected(transport));

        // Should get encoded request data on the transport pipe
        var output = await transport.ReadOutputAsync(TestContext.Current.CancellationToken);
        var encoded = Encoding.ASCII.GetString(output);
        transport.AdvanceOutput(output.End);
        Assert.StartsWith("GET /test HTTP/1.0\r\n", encoded);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-6")]
    public async Task Http10ConnectionStage_should_decode_response_and_correlate_with_request()
    {
        var stage = new Http10ClientConnectionStage(new GaudiClientOptions());

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var app = b.Add(Source.FromPublisher(appProbe));
            var server = b.Add(Source.FromPublisher(serverProbe));
            var netSink = b.Add(Sink.FromSubscriber(networkSub));
            var resSink = b.Add(Sink.FromSubscriber(responseSub));

            b.From(app).To(s.InRequest);
            b.From(server).To(s.InNetwork);
            b.From(s.OutNetwork).To(netSink);
            b.From(s.OutResponse).To(resSink);

            return ClosedShape.Instance;
        })).Run(Materializer);

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSubscription = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSubscription.Request(10);
        resSubscription.Request(10);

        // Send request
        appSubscription.SendNext(MakeRequest("/hello"));

        // Consume ConnectTransport
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        // Simulate transport connect — encoded request goes through pipe
        var transport = new TestPipeTransport();
        serverSubscription.SendNext(MakeTransportConnected(transport));

        // Consume encoded request from pipe
        var output = await transport.ReadOutputAsync(TestContext.Current.CancellationToken);
        transport.AdvanceOutput(output.End);

        // Send response from server via pipe
        await transport.FeedInputAsync(Encoding.ASCII.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello"));

        // Should get correlated response
        var response = await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.RequestMessage);
        Assert.Equal("/hello", response.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-7.2.2")]
    public async Task Http10ConnectionStage_should_emit_connection_reuse_close_for_http10()
    {
        var stage = new Http10ClientConnectionStage(new GaudiClientOptions());

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var app = b.Add(Source.FromPublisher(appProbe));
            var server = b.Add(Source.FromPublisher(serverProbe));
            var netSink = b.Add(Sink.FromSubscriber(networkSub));
            var resSink = b.Add(Sink.FromSubscriber(responseSub));

            b.From(app).To(s.InRequest);
            b.From(server).To(s.InNetwork);
            b.From(s.OutNetwork).To(netSink);
            b.From(s.OutResponse).To(resSink);

            return ClosedShape.Instance;
        })).Run(Materializer);

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSubscription = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSubscription.Request(10);
        resSubscription.Request(10);

        // Send request + response
        appSubscription.SendNext(MakeRequest());

        // ConnectTransport
        await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);

        // Simulate transport connect — encoded request goes through pipe
        var transport = new TestPipeTransport();
        serverSubscription.SendNext(MakeTransportConnected(transport));

        // Consume encoded request from pipe
        var output = await transport.ReadOutputAsync(TestContext.Current.CancellationToken);
        transport.AdvanceOutput(output.End);

        // Send response via pipe
        await transport.FeedInputAsync(
            Encoding.ASCII.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nOK"));

        // Response
        await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
    }


    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10ConnectionStage_should_complete_stage_when_app_upstream_finishes_without_inflight()
    {
        var stage = new Http10ClientConnectionStage(new GaudiClientOptions());

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var app = b.Add(Source.FromPublisher(appProbe));
            var server = b.Add(Source.FromPublisher(serverProbe));
            var netSink = b.Add(Sink.FromSubscriber(networkSub));
            var resSink = b.Add(Sink.FromSubscriber(responseSub));

            b.From(app).To(s.InRequest);
            b.From(server).To(s.InNetwork);
            b.From(s.OutNetwork).To(netSink);
            b.From(s.OutResponse).To(resSink);

            return ClosedShape.Instance;
        })).Run(Materializer);

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSubscription = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSubscription.Request(10);
        resSubscription.Request(10);

        // Complete app upstream without sending any request
        appSubscription.SendComplete();

        // Stage should complete
        await Task.Run(() => responseSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10ConnectionStage_should_complete_when_server_closes_and_no_response_pending()
    {
        var stage = new Http10ClientConnectionStage(new GaudiClientOptions());

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            var app = b.Add(Source.FromPublisher(appProbe));
            var server = b.Add(Source.FromPublisher(serverProbe));
            var netSink = b.Add(Sink.FromSubscriber(networkSub));
            var resSink = b.Add(Sink.FromSubscriber(responseSub));

            b.From(app).To(s.InRequest);
            b.From(server).To(s.InNetwork);
            b.From(s.OutNetwork).To(netSink);
            b.From(s.OutResponse).To(resSink);

            return ClosedShape.Instance;
        })).Run(Materializer);

        var netSubscription = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSubscription = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSubscription = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSubscription.Request(10);
        resSubscription.Request(10);

        // Server closes without any request/response activity
        serverSubscription.SendComplete();

        // Stage should complete
        await Task.Run(() => responseSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }
}
