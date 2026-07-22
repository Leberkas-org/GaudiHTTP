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

namespace GaudiHTTP.Tests.Protocol.Syntax.Http11.Stages;

public sealed class Http11ConnectionStageReconnectSpec : StreamTestBase
{
    private static HttpRequestMessage MakeRequest(string path = "/")
        => new(HttpMethod.Get, new Uri($"http://example.com{path}"))
        {
            Version = new Version(1, 1)
        };

    private static TransportConnected MakeTransportConnected(
        TestPipeTransport transport, IPEndPoint local, IPEndPoint remote)
        => new(new ConnectionInfo(local, remote, TransportProtocol.Tcp), transport);

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionStage_should_reconnect_and_replay_request_on_connection_drop()
    {
        var stage = new Http11ClientConnectionStage(new GaudiClientOptions
        {
            Http1 =
            {
                MaxPipelineDepth = 1,
                MaxReconnectAttempts = 1
            }
        });

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            b.From(Source.FromPublisher(appProbe)).To(s.InRequest);
            b.From(Source.FromPublisher(serverProbe)).To(s.InNetwork);
            b.From(s.OutNetwork).To(Sink.FromSubscriber(networkSub));
            b.From(s.OutResponse).To(Sink.FromSubscriber(responseSub));
            return ClosedShape.Instance;
        })).Run(Materializer);

        var netSub = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSub = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSub = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSub = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSub.Request(20);
        resSub.Request(10);

        // Send a request
        appSub.SendNext(MakeRequest());

        // Consume ConnectTransport
        var item0 = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var connect0 = Assert.IsType<ConnectTransport>(item0);

        // Simulate initial connect success — request encoding is deferred until TransportConnected
        var transport1 = new TestPipeTransport();
        var initialRemote = new IPEndPoint(IPAddress.Loopback, connect0.Options.Port);
        var initialLocal = new IPEndPoint(IPAddress.Loopback, 0);
        serverSub.SendNext(MakeTransportConnected(transport1, initialLocal, initialRemote));

        // Consume encoded request from pipe
        var output = await transport1.ReadOutputAsync(TestContext.Current.CancellationToken);
        transport1.AdvanceOutput(output.End);

        // Connection drops while request is in-flight
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Error));

        // Stage must emit ConnectTransport
        var reconnectRaw = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var reconnect = Assert.IsType<ConnectTransport>(reconnectRaw);

        // Simulate reconnect success → new transport for the new connection
        var transport2 = new TestPipeTransport();
        var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, reconnect.Options.Port);
        var localEndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        serverSub.SendNext(MakeTransportConnected(transport2, localEndPoint, remoteEndPoint));

        // Stage must replay the request — consume encoded data from pipe
        output = await transport2.ReadOutputAsync(TestContext.Current.CancellationToken);
        transport2.AdvanceOutput(output.End);

        // Now respond normally
        await transport2.FeedInputAsync(Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello"));

        var response = await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionStage_should_complete_stage_when_max_reconnect_attempts_exceeded()
    {
        var stage = new Http11ClientConnectionStage(new GaudiClientOptions
        {
            Http1 =
            {
                MaxPipelineDepth = 1,
                MaxReconnectAttempts = 1
            }
        });

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            b.From(Source.FromPublisher(appProbe)).To(s.InRequest);
            b.From(Source.FromPublisher(serverProbe)).To(s.InNetwork);
            b.From(s.OutNetwork).To(Sink.FromSubscriber(networkSub));
            b.From(s.OutResponse).To(Sink.FromSubscriber(responseSub));
            return ClosedShape.Instance;
        })).Run(Materializer);

        var netSub = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSub = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var appSub = await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSub = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSub.Request(20);
        resSub.Request(10);

        appSub.SendNext(MakeRequest());
        var connect0Raw = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken); // ConnectTransport
        var connect0 = Assert.IsType<ConnectTransport>(connect0Raw);

        var transport1 = new TestPipeTransport();
        var initialRemote = new IPEndPoint(IPAddress.Loopback, connect0.Options.Port);
        var initialLocal = new IPEndPoint(IPAddress.Loopback, 0);
        serverSub.SendNext(MakeTransportConnected(transport1, initialLocal, initialRemote));

        // Consume encoded request from pipe
        var output = await transport1.ReadOutputAsync(TestContext.Current.CancellationToken);
        transport1.AdvanceOutput(output.End);

        // First drop → reconnect attempt 1 (hits max immediately)
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Error));
        var reconnectRaw = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectTransport>(reconnectRaw);

        // Reconnect fails → TransportDisconnected again (attempt 2 exceeds max of 1)
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Error));
        serverSub.SendComplete();

        // Stage should complete
        await Task.Run(() => responseSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC9112-9.3")]
    public async Task Http11ConnectionStage_should_not_reconnect_when_no_inflight_request_on_close()
    {
        var stage = new Http11ClientConnectionStage(new GaudiClientOptions
        {
            Http1 =
            {
                MaxPipelineDepth = 1,
                MaxReconnectAttempts = 1
            }
        });

        var appProbe = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var serverProbe = this.CreateManualPublisherProbe<ITransportInbound>();
        var networkSub = this.CreateManualSubscriberProbe<ITransportOutbound>();
        var responseSub = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var s = b.Add(stage);
            b.From(Source.FromPublisher(appProbe)).To(s.InRequest);
            b.From(Source.FromPublisher(serverProbe)).To(s.InNetwork);
            b.From(s.OutNetwork).To(Sink.FromSubscriber(networkSub));
            b.From(s.OutResponse).To(Sink.FromSubscriber(responseSub));
            return ClosedShape.Instance;
        })).Run(Materializer);

        var netSub = await networkSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var resSub = await responseSub.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        await appProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);
        var serverSub = await serverProbe.ExpectSubscriptionAsync(TestContext.Current.CancellationToken);

        netSub.Request(20);
        resSub.Request(10);

        // No requests sent — connection just closes cleanly
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Graceful));
        serverSub.SendComplete();

        // Stage completes when server upstream finishes
        await Task.Run(() => networkSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }
}