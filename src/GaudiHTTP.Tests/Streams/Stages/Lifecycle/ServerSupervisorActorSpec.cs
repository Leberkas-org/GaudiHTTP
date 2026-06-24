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
        => Flow.Create<IFeatureCollection>();

    private sealed class DummyListenerFactory : IListenerFactory
    {
        private readonly int _boundPort;

        public DummyListenerFactory(int boundPort = 8080)
        {
            _boundPort = boundPort;
        }

        public Source<Flow<ITransportOutbound, ITransportInbound, NotUsed>, Task<int>> Bind(ListenerOptions options)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs.SetResult(_boundPort);
            return Source.Empty<Flow<ITransportOutbound, ITransportInbound, NotUsed>>()
                .MapMaterializedValue(_ => tcs.Task);
        }
    }

    private sealed class FailingListenerFactory : IListenerFactory
    {
        public Source<Flow<ITransportOutbound, ITransportInbound, NotUsed>, Task<int>> Bind(ListenerOptions options)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs.SetException(new InvalidOperationException("port already in use"));
            return Source.Empty<Flow<ITransportOutbound, ITransportInbound, NotUsed>>()
                .MapMaterializedValue(_ => tcs.Task);
        }
    }

    private static ServerSupervisorActor.StartServer StartMsg(params ListenerBinding[] bindings)
        => new(PassthroughBridge(), new TurboServerOptions(), bindings);

    [Fact(Timeout = 5000)]
    public void Start_should_reply_ListenersReady_when_all_listeners_bind()
    {
        var supervisor = Sys.ActorOf(Props.Create(() => new ServerSupervisorActor()));

        supervisor.Tell(StartMsg(new ListenerBinding
        {
            Factory = new DummyListenerFactory(9000),
            Options = new TcpListenerOptions { Host = "localhost", Port = 0 }
        }), TestActor);

        var ready = ExpectMsg<ServerSupervisorActor.ListenersReady>(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(ready.BoundPorts);
        Assert.Equal(9000, ready.BoundPorts[0]);
    }

    [Fact(Timeout = 5000)]
    public void Start_should_reply_ListenersFailed_when_any_listener_dies()
    {
        var supervisor = Sys.ActorOf(Props.Create(() => new ServerSupervisorActor()));

        supervisor.Tell(StartMsg(new ListenerBinding
        {
            Factory = new FailingListenerFactory(),
            Options = new TcpListenerOptions { Host = "localhost", Port = 0 }
        }), TestActor);

        var failed = ExpectMsg<ServerSupervisorActor.ListenersFailed>(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(failed.Error);
    }

    [Fact(Timeout = 5000)]
    public void Start_should_reply_ListenersReady_immediately_when_no_bindings()
    {
        var supervisor = Sys.ActorOf(Props.Create(() => new ServerSupervisorActor()));

        supervisor.Tell(StartMsg(), TestActor);

        var ready = ExpectMsg<ServerSupervisorActor.ListenersReady>(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(ready.BoundPorts);
    }

    [Fact(Timeout = 5000)]
    public void Drain_should_reply_DrainComplete_with_TimedOut_false_on_clean_drain()
    {
        var supervisor = Sys.ActorOf(Props.Create(() => new ServerSupervisorActor()));

        supervisor.Tell(StartMsg(new ListenerBinding
        {
            Factory = new DummyListenerFactory(),
            Options = new TcpListenerOptions { Host = "localhost", Port = 0 }
        }), TestActor);

        ExpectMsg<ServerSupervisorActor.ListenersReady>(
            cancellationToken: TestContext.Current.CancellationToken);

        supervisor.Tell(new ServerSupervisorActor.BeginDrain(TimeSpan.FromSeconds(5)), TestActor);

        var drain = ExpectMsg<ServerSupervisorActor.DrainComplete>(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(drain.TimedOut);
    }

    [Fact(Timeout = 5000)]
    public void Drain_should_reply_immediately_when_no_listeners()
    {
        var supervisor = Sys.ActorOf(Props.Create(() => new ServerSupervisorActor()));

        supervisor.Tell(new ServerSupervisorActor.BeginDrain(TimeSpan.FromSeconds(5)), TestActor);

        var drain = ExpectMsg<ServerSupervisorActor.DrainComplete>(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(drain.TimedOut);
    }
}
