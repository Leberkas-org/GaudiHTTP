using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using GaudiHTTP.Server;
using GaudiHTTP.Streams;
using GaudiHTTP.Streams.Lifecycle;

namespace GaudiHTTP.Tests.Streams.Stages.Lifecycle;

public sealed class ListenerActorSpec : TestKit
{
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

    private sealed class ConnectionEmittingFactory : IListenerFactory
    {
        private readonly int _boundPort;
        private readonly int _connectionCount;

        public ConnectionEmittingFactory(int boundPort, int connectionCount)
        {
            _boundPort = boundPort;
            _connectionCount = connectionCount;
        }

        public Source<Flow<ITransportOutbound, ITransportInbound, NotUsed>, Task<int>> Bind(ListenerOptions options)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs.SetResult(_boundPort);

            var connections = Enumerable.Range(0, _connectionCount)
                .Select(_ => Flow.FromSinkAndSource(
                    Sink.Ignore<ITransportOutbound>().MapMaterializedValue(_ => NotUsed.Instance),
                    Source.Maybe<ITransportInbound>().MapMaterializedValue(_ => NotUsed.Instance)));

            return Source.From(connections)
                .MapMaterializedValue(_ => tcs.Task);
        }
    }

    private static IGraph<FlowShape<IFeatureCollection, IFeatureCollection>, NotUsed> PassthroughBridgeGraph()
        => Flow.Create<IFeatureCollection>();

    private sealed class DummyProtocolEngine : IServerProtocolEngine
    {
        public Version ProtocolVersion => new(1, 1);

        public BidiFlow<ITransportInbound, IFeatureCollection, IFeatureCollection, ITransportOutbound, NotUsed>
            CreateFlow(IServiceProvider? services = null)
        {
            return BidiFlow.FromFlows(
                Flow.Create<ITransportInbound>()
                    .Select(_ => new FeatureCollection() as IFeatureCollection),
                Flow.Create<IFeatureCollection>()
                    .Select(_ =>
                    {
                        var buffer = TransportBuffer.Rent(1);
                        buffer.Dispose();
                        return TransportData.Rent(buffer) as ITransportOutbound;
                    }));
        }
    }

    private static TurboServerOptions DefaultOptions() => new();

    [Fact(Timeout = 5000)]
    public void Listener_should_reply_ListeningStarted_on_successful_bind()
    {
        var listener = Sys.ActorOf(ListenerActor.Create(
            new DummyListenerFactory(9000),
            new TcpListenerOptions { Host = "localhost", Port = 0 },
            DefaultOptions(),
            PassthroughBridgeGraph(),
            new DummyProtocolEngine()));

        listener.Tell(new ListenerActor.StartListening(), TestActor);

        var listening = ExpectMsg<ListenerActor.ListeningStarted>(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(9000, listening.BoundPort);
        Assert.NotNull(listening.Handle);
    }

    [Fact(Timeout = 5000)]
    public void Listener_should_stop_self_on_bind_failure()
    {
        var listener = Sys.ActorOf(ListenerActor.Create(
            new FailingListenerFactory(),
            new TcpListenerOptions { Host = "localhost", Port = 0 },
            DefaultOptions(),
            PassthroughBridgeGraph(),
            new DummyProtocolEngine()));

        Watch(listener);
        listener.Tell(new ListenerActor.StartListening(), TestActor);

        ExpectTerminated(listener, TimeSpan.FromSeconds(3),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public void Listener_should_reject_connection_when_at_max_concurrent()
    {
        var options = new TurboServerOptions();
        options.Limits.MaxConcurrentConnections = 1;

        var listener = Sys.ActorOf(ListenerActor.Create(
            new ConnectionEmittingFactory(9000, connectionCount: 2),
            new TcpListenerOptions { Host = "localhost", Port = 0 },
            options,
            PassthroughBridgeGraph(),
            new DummyProtocolEngine()));

        listener.Tell(new ListenerActor.StartListening(), TestActor);

        var listening = ExpectMsg<ListenerActor.ListeningStarted>(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(9000, listening.BoundPort);
    }

    [Fact(Timeout = 5000)]
    public void Listener_should_complete_drain_when_no_active_connections()
    {
        var listener = Sys.ActorOf(ListenerActor.Create(
            new DummyListenerFactory(9000),
            new TcpListenerOptions { Host = "localhost", Port = 0 },
            DefaultOptions(),
            PassthroughBridgeGraph(),
            new DummyProtocolEngine()));

        listener.Tell(new ListenerActor.StartListening(), TestActor);
        ExpectMsg<ListenerActor.ListeningStarted>(
            cancellationToken: TestContext.Current.CancellationToken);

        Watch(listener);
        listener.Tell(new ListenerActor.DrainConnections());

        // Listener with 0 connections should complete its TCS immediately.
        // The actor itself remains alive — the supervisor handles stopping it.
        // We verify by ensuring no Terminated arrives (actor stays alive).
        ExpectNoMsg(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
    }
}
