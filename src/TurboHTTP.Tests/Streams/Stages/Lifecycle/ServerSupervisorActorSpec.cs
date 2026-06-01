using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.Streams.Lifecycle;

namespace TurboHTTP.Tests.Streams.Stages.Lifecycle;

public sealed class ServerSupervisorActorSpec : TestKit
{
    private static IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> PassthroughBridge()
    {
        return Flow.Create<IFeatureCollection>();
    }

    [Fact(Timeout = 5000)]
    public void Supervisor_should_start_server_and_report_ready()
    {
        var supervisor = Sys.ActorOf(Props.Create(() => new ServerSupervisorActor()));

        var bindings = new List<ListenerBinding>
        {
            new()
            {
                Factory = new DummyListenerFactory(),
                Options = new TcpListenerOptions { Host = "localhost", Port = 8080 }
            }
        };

        supervisor.Tell(
            new ServerSupervisorActor.StartServer(PassthroughBridge(), new TurboServerOptions(), bindings),
            TestActor);

        var ready = ExpectMsg<ServerSupervisorActor.ListenersReady>(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(ready.BoundPorts);
    }

    [Fact(Timeout = 5000)]
    public void Supervisor_should_drain_after_start()
    {
        var supervisor = Sys.ActorOf(Props.Create(() => new ServerSupervisorActor()));

        var bindings = new List<ListenerBinding>
        {
            new()
            {
                Factory = new DummyListenerFactory(),
                Options = new TcpListenerOptions { Host = "localhost", Port = 8080 }
            }
        };

        supervisor.Tell(
            new ServerSupervisorActor.StartServer(PassthroughBridge(), new TurboServerOptions(), bindings),
            TestActor);

        ExpectMsg<ServerSupervisorActor.ListenersReady>(
            cancellationToken: TestContext.Current.CancellationToken);

        supervisor.Tell(new ServerSupervisorActor.BeginDrain(TimeSpan.FromSeconds(5)), TestActor);

        ExpectMsg<ServerSupervisorActor.DrainComplete>(
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private sealed class DummyListenerFactory : IListenerFactory
    {
        public Source<Flow<ITransportOutbound, ITransportInbound, NotUsed>, Task<int>> Bind(ListenerOptions options)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs.SetResult(options.Port);
            return Source.Empty<Flow<ITransportOutbound, ITransportInbound, NotUsed>>()
                .MapMaterializedValue(_ => tcs.Task);
        }
    }
}
