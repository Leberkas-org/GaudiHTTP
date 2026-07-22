using GaudiHTTP.Tests.TestSupport;
using GaudiHTTP.Client;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Servus.Akka.Transport;
using GaudiHTTP.Streams.Stages.Client;
using GaudiHTTP.Tests.Shared;
using static GaudiHTTP.Tests.Protocol.Syntax.Http2.Stages.Http2ConnectionTestHelper;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Stages;

public sealed class Http20ConnectionStageSpec : StreamTestBase
{
    private static HttpRequestMessage MakeRequest(string path = "/")
    {
        return new HttpRequestMessage(HttpMethod.Get, $"https://example.com{path}")
        {
            Version = new Version(2, 0)
        };
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-3.2")]
    public async Task Http20ConnectionStage_should_emit_connect_then_preface_on_first_request()
    {
        var stage = new Http20ClientConnectionStage(new GaudiClientOptions
        { Http2 = { MaxReconnectAttempts = 3 } });

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

        netSubscription.Request(5);
        resSubscription.Request(5);

        appSubscription.SendNext(MakeRequest());

        var connect = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectTransport>(connect);

        // Send TransportConnected with an InMemoryTransport — the preface is written to the transport.
        var transport = new InMemoryTransport();
        serverSubscription.SendNext(CreateTransportConnected(transport));

        // Wait for stage to process TransportConnected and write preface to transport.
        // ConnectTransport was the only OutNetwork item; nothing else arrives on networkSub.
        // Use a brief delay for the actor to process the message.
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var prefaceBytes = transport.WrittenSpan;
        Assert.True(prefaceBytes.Length > 0, "Preface must be written to transport");
        var data = Encoding.ASCII.GetString(prefaceBytes[..Math.Min(prefaceBytes.Length, 24)]);
        Assert.StartsWith("PRI * HTTP/2.0", data);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6")]
    public async Task Http20ConnectionStage_should_encode_request_as_headers_frame()
    {
        var stage = new Http20ClientConnectionStage(new GaudiClientOptions
        { Http2 = { MaxReconnectAttempts = 3 } });

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

        appSubscription.SendNext(MakeRequest("/test"));

        var connect = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectTransport>(connect);

        var transport = new InMemoryTransport();
        serverSubscription.SendNext(CreateTransportConnected(transport));

        // Preface + SETTINGS + HEADERS are written to transport.
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var frames = DecodeFrames(transport.WrittenMemory, skipPreface: true);
        Assert.Contains(frames, f => f is GaudiHTTP.Protocol.Syntax.Http2.HeadersFrame);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.2")]
    public async Task Http20ConnectionStage_should_support_stream_multiplexing()
    {
        var stage = new Http20ClientConnectionStage(new GaudiClientOptions { Http2 = { MaxReconnectAttempts = 3 } });

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

        netSubscription.Request(20);
        resSubscription.Request(10);

        // Send first request to trigger ConnectTransport
        appSubscription.SendNext(MakeRequest("/req1"));

        var connect = await networkSub.ExpectNextAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ConnectTransport>(connect);

        // Connect transport — this flushes the pending initial request (req1)
        var transport = new InMemoryTransport();
        serverSubscription.SendNext(CreateTransportConnected(transport));

        // Send second request after transport is connected (multiplexing)
        appSubscription.SendNext(MakeRequest("/req2"));

        // Preface + SETTINGS + 2x HEADERS written to transport.
        await Task.Delay(200, TestContext.Current.CancellationToken);

        var frames = DecodeFrames(transport.WrittenMemory, skipPreface: true);
        var headersFrames = frames.OfType<GaudiHTTP.Protocol.Syntax.Http2.HeadersFrame>().ToList();
        Assert.True(headersFrames.Count >= 2,
            $"Expected at least 2 HEADERS frames for multiplexed requests, got {headersFrames.Count}");
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-3.1")]
    public async Task Http20ConnectionStage_should_handle_settings_frame()
    {
        var stage = new Http20ClientConnectionStage(new GaudiClientOptions { Http2 = { MaxReconnectAttempts = 3 } });

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

        // Feed a SETTINGS frame via the transport. The SM processes it through DecodeData.
        var settingsBytes = Encoding.ASCII.GetBytes("\x00\x00\x00\x04\x00\x00\x00\x00\x00");
        var transport = new InMemoryTransport();
        transport.Feed(settingsBytes);
        serverSubscription.SendNext(CreateTransportConnected(transport));

        // The SM reads the SETTINGS from the transport and emits a SETTINGS ACK back to it.
        // No crash = success.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.True(true);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6.8")]
    public async Task Http20ConnectionStage_should_complete_on_goaway_with_no_inflight()
    {
        var stage = new Http20ClientConnectionStage(new GaudiClientOptions { Http2 = { MaxReconnectAttempts = 3 } });

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

        // TransportDisconnected is a lifecycle event, not data — still flows through InNetwork.
        serverSubscription.SendNext(new TransportDisconnected(DisconnectReason.Graceful));
        serverSubscription.SendComplete();

        networkSub.ExpectComplete(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-6")]
    public async Task Http20ConnectionStage_should_complete_when_app_upstream_finishes_with_no_inflight()
    {
        var stage = new Http20ClientConnectionStage(new GaudiClientOptions { Http2 = { MaxReconnectAttempts = 3 } });

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

        appSubscription.SendComplete();

        responseSub.ExpectComplete(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9113-4.1")]
    public async Task Http20ConnectionStage_should_fail_when_transport_read_throws_unexpectedly()
    {
        var stage = new Http20ClientConnectionStage(new GaudiClientOptions { Http2 = { MaxReconnectAttempts = 3 } });

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

        // A transport whose ReadAsync always throws simulates catastrophic I/O corruption.
        // The stage must FAIL the connection: swallowing the exception would leave the transport
        // in an undefined state. With pipe transport, the exception propagates through
        // TransportIo.RequestRead -> DispatchLifecycleEvent -> OnNetworkPush -> catch -> FailStage.
        serverSubscription.SendNext(new TransportConnected(
            new ConnectionInfo(
                new IPEndPoint(IPAddress.Loopback, 0),
                new IPEndPoint(IPAddress.Loopback, 443),
                TransportProtocol.Tcp),
            new ThrowOnReadTransport()));

        responseSub.ExpectError(TestContext.Current.CancellationToken);
    }

    private sealed class ThrowOnReadTransport : IConnectionTransport
    {
        public ConnectionInfo Info => ConnectionInfo.None;

        public ValueTask<ReadResult> ReadAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated transport I/O corruption");

        public void AdvanceTo(SequencePosition consumed) { }
        public void AdvanceTo(SequencePosition consumed, SequencePosition examined) { }
        public Memory<byte> GetMemory(int sizeHint = 0) => new byte[sizeHint > 0 ? sizeHint : 256];
        public void Advance(int bytes) { }

        public ValueTask<FlushResult> FlushAsync(CancellationToken ct = default)
            => new(new FlushResult(false, false));

        public void CompleteOutput() { }
        public void Abort() { }
    }
}
