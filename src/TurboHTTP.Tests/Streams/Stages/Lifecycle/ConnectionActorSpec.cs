using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using System.Net;
using TurboHTTP.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Lifecycle;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Streams.Stages.Lifecycle;

public sealed class ConnectionActorSpec : TestKit
{
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

    private ServerPipeline CreatePassthroughPipeline()
    {
        var options = new TurboServerOptions { Limits = { MaxConcurrentRequests = 0 } };
        var killSwitch = KillSwitches.Shared("connactor-test-pipeline");
        return ServerPipeline.Materialize(
            Flow.Create<IFeatureCollection>(), options, killSwitch, Sys.Materializer(), Sys);
    }

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

    [Fact(Timeout = 10000)]
    public void ConnectionActor_should_materialize_and_complete_on_connection_close()
    {
        var pipeline = CreatePassthroughPipeline();
        var engine = new PassthroughEngine();
        var options = new TurboServerOptions();

        var actor = Sys.ActorOf(ConnectionActor.Props(
            1, FakeConnectionFlow(), pipeline, engine, options));

        Watch(actor);
        ExpectTerminated(actor, TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public void ConnectionActor_should_drain_on_drain_message()
    {
        var pipeline = CreatePassthroughPipeline();
        var engine = new PassthroughEngine();
        var options = new TurboServerOptions();

        var actor = Sys.ActorOf(ConnectionActor.Props(
            1, HangingConnectionFlow(), pipeline, engine, options));

        Watch(actor);
        actor.Tell(new ConnectionActor.Drain());
        ExpectTerminated(actor, TimeSpan.FromSeconds(5), cancellationToken: TestContext.Current.CancellationToken);
    }
}
