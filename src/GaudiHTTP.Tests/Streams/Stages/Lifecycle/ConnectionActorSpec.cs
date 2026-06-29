using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using System.Net;
using GaudiHTTP.Server;
using GaudiHTTP.Streams;
using GaudiHTTP.Streams.Lifecycle;

namespace GaudiHTTP.Tests.Streams.Stages.Lifecycle;

public sealed class ConnectionActorSpec : TestKit
{
    public ConnectionActorSpec() : base("akka.loglevel = INFO")
    {
    }

    private sealed class PassthroughEngine : IServerProtocolEngine
    {
        public Version ProtocolVersion => new(1, 1);

        public BidiFlow<ITransportInbound, IFeatureCollection, IFeatureCollection, ITransportOutbound, NotUsed>
            CreateFlow(IServiceProvider? services = null)
        {
            var top = Flow.Create<ITransportInbound>()
                .Select(_ => (IFeatureCollection)new FeatureCollection());
            var bottom = Flow.Create<IFeatureCollection>()
                .Select(_ => new DisconnectTransport(DisconnectReason.Graceful) as ITransportOutbound);
            return BidiFlow.FromFlows(top, bottom);
        }
    }

    private static IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> PassthroughBridgeGraph()
        => Flow.Create<IFeatureCollection>();

    private static Flow<ITransportOutbound, ITransportInbound, NotUsed> FakeConnectionFlow()
    {
        var connInfo = new ConnectionInfo(
            new IPEndPoint(IPAddress.Loopback, 8080),
            new IPEndPoint(IPAddress.Loopback, 8081),
            TransportProtocol.Tcp);

        return Flow.FromSinkAndSource(
            Sink.Ignore<ITransportOutbound>().MapMaterializedValue(_ => NotUsed.Instance),
            Source.Single<ITransportInbound>(new TransportConnected(connInfo)));
    }

    private static Flow<ITransportOutbound, ITransportInbound, NotUsed> HangingConnectionFlow()
    {
        return Flow.FromSinkAndSource(
            Sink.Ignore<ITransportOutbound>().MapMaterializedValue(_ => NotUsed.Instance),
            Source.Maybe<ITransportInbound>().MapMaterializedValue(_ => NotUsed.Instance));
    }

    private static Flow<ITransportOutbound, ITransportInbound, NotUsed> FailingConnectionFlow()
    {
        return Flow.FromSinkAndSource(
            Sink.Ignore<ITransportOutbound>().MapMaterializedValue(_ => NotUsed.Instance),
            Source.Failed<ITransportInbound>(new InvalidOperationException("connection failed")));
    }

    [Fact(Timeout = 10000)]
    public void ConnectionActor_should_stop_on_stream_completion()
    {
        var actor = Sys.ActorOf(ConnectionActor.Props(
            1, FakeConnectionFlow(), PassthroughBridgeGraph(), new PassthroughEngine(), new GaudiServerOptions()));

        Watch(actor);
        ExpectTerminated(actor, TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public void ConnectionActor_should_stop_on_stream_failure()
    {
        var actor = Sys.ActorOf(ConnectionActor.Props(
            2, FailingConnectionFlow(), PassthroughBridgeGraph(), new PassthroughEngine(), new GaudiServerOptions()));

        Watch(actor);
        ExpectTerminated(actor, TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public void ConnectionActor_should_drain_on_drain_message()
    {
        var actor = Sys.ActorOf(ConnectionActor.Props(
            3, HangingConnectionFlow(), PassthroughBridgeGraph(), new PassthroughEngine(), new GaudiServerOptions()));

        Watch(actor);
        actor.Tell(new ConnectionActor.Drain());
        ExpectTerminated(actor, TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public void ConnectionActor_should_log_lifecycle_under_configured_category()
    {
        // UseConnectionLogging(category): connection accepted events must be logged under a logger
        // whose source IS the configured category. Previously the category was completely dead.
        const string category = "GaudiHTTP.ConnLogTest";
        var probe = CreateTestProbe();
        Sys.EventStream.Subscribe(probe.Ref, typeof(Akka.Event.Info));

        Sys.ActorOf(ConnectionActor.Props(
            7, FakeConnectionFlow(), PassthroughBridgeGraph(), new PassthroughEngine(),
            new GaudiServerOptions(), services: null, loggingCategory: category));

        var info = probe.FishForMessage<Akka.Event.Info>(
            m => m.Message?.ToString()?.Contains("Connection 7 accepted") == true,
            TimeSpan.FromSeconds(5));

        Assert.StartsWith(category, info.LogSource);
    }
}
