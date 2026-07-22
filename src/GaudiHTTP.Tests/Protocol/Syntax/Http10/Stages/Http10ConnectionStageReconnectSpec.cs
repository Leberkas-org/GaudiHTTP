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

public sealed class Http10ConnectionStageReconnectSpec : StreamTestBase
{
    private static HttpRequestMessage MakeRequest()
        => new(HttpMethod.Get, new Uri("http://example.com/"))
        {
            Version = new Version(1, 0)
        };

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10ConnectionStage_should_reconnect_and_replay_request_on_connection_drop()
    {
        var stage = new Http10ClientConnectionStage(new GaudiClientOptions { Http1 = { MaxReconnectAttempts = 3 } });

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
        serverSub.SendNext(
            new TransportConnected(new ConnectionInfo(initialLocal, initialRemote, TransportProtocol.Tcp), transport1));

        // Consume encoded request from pipe
        var output1 = await transport1.ReadOutputAsync(TestContext.Current.CancellationToken);
        transport1.AdvanceOutput(output1.End);

        // Connection drops while request is in-flight
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Error));

        // Stage must emit ConnectTransport (not fail or complete)
        var reconnectRaw = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        var reconnect = Assert.IsType<ConnectTransport>(reconnectRaw);

        // Simulate TcpConnectionStage reconnect success — sends TransportConnected with new transport
        var transport2 = new TestPipeTransport();
        var remoteEndPoint = new IPEndPoint(IPAddress.Loopback, reconnect.Options.Port);
        var localEndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        serverSub.SendNext(
            new TransportConnected(new ConnectionInfo(localEndPoint, remoteEndPoint, TransportProtocol.Tcp), transport2));

        // Stage must replay the request — expect encoded data on the new transport pipe
        var output2 = await transport2.ReadOutputAsync(TestContext.Current.CancellationToken);
        transport2.AdvanceOutput(output2.End);

        // Now respond normally via pipe
        await transport2.FeedInputAsync(Encoding.ASCII.GetBytes("HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello"));

        var response = await responseSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10ConnectionStage_should_complete_stage_when_max_reconnect_attempts_exceeded()
    {
        var stage = new Http10ClientConnectionStage(new GaudiClientOptions { Http1 = { MaxReconnectAttempts = 1 } });

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
        serverSub.SendNext(
            new TransportConnected(new ConnectionInfo(initialLocal, initialRemote, TransportProtocol.Tcp), transport1));

        // Consume encoded request from pipe
        var output1 = await transport1.ReadOutputAsync(TestContext.Current.CancellationToken);
        transport1.AdvanceOutput(output1.End);

        // First drop -> reconnect attempt 1 (hits max immediately)
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Error));
        var reconnectRaw = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectTransport>(reconnectRaw);

        // Reconnect fails -> TransportDisconnected again (attempt 2 exceeds max of 1)
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Error));

        // Transport source completes after final disconnect
        serverSub.SendComplete();

        // Stage should complete
        await Task.Run(() => responseSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    [Trait("RFC", "RFC1945-4")]
    public async Task Http10ConnectionStage_should_not_reconnect_when_no_inflight_request_on_close()
    {
        var stage = new Http10ClientConnectionStage(new GaudiClientOptions { Http1 = { MaxReconnectAttempts = 1 } });

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

        // No requests sent — connection just closes
        serverSub.SendNext(new TransportDisconnected(DisconnectReason.Graceful));
        serverSub.SendComplete();

        // Stage completes when server upstream finishes
        await Task.Run(() => networkSub.ExpectComplete(), TestContext.Current.CancellationToken);
    }
}
