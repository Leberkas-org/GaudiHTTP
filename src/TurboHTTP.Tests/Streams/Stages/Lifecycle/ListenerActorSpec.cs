using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Lifecycle;
using TurboHTTP.Streams.Stages.Server;
using Xunit;

namespace TurboHTTP.Tests.Streams.Stages.Lifecycle;

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

    private PipelineHandles CreateDummyPipelineHandles()
    {
        var dispatcher = new FairShareDispatcher(0, 0);
        var requestSink = Sink.Ignore<IFeatureCollection>().MapMaterializedValue(_ => NotUsed.Instance);
        var responseHub = new ResponseDispatcherHub();
        var responseDispatcher = Source.Empty<IFeatureCollection>()
            .ToMaterialized(responseHub, Keep.Right)
            .Run(Sys.Materializer());
        return new PipelineHandles(requestSink, responseDispatcher, dispatcher);
    }

    private sealed class DummyProtocolEngine : IServerProtocolEngine
    {
        public Version ProtocolVersion => new(1, 1);

        public BidiFlow<ITransportInbound, IFeatureCollection, IFeatureCollection, ITransportOutbound, NotUsed> CreateFlow(
            IServiceProvider? services = null)
        {
            return BidiFlow.FromFlows(
                Flow.Create<ITransportInbound>()
                    .Select(_ => new FeatureCollection() as IFeatureCollection),
                Flow.Create<IFeatureCollection>()
                    .Select(_ =>
                    {
                        var buffer = TransportBuffer.Rent(1);
                        buffer.Dispose();
                        return new TransportData(buffer) as ITransportOutbound;
                    }));
        }
    }

    [Fact(Timeout = 5000)]
    public void Listener_should_bind_and_report_listening_started()
    {
        var listener = Sys.ActorOf(ListenerActor.Create(
            new DummyListenerFactory(9000),
            new TcpListenerOptions { Host = "localhost", Port = 0 },
            new TurboServerOptions(),
            CreateDummyPipelineHandles(),
            new DummyProtocolEngine()));

        listener.Tell(new ListenerActor.StartListening(), TestActor);

        var listening = ExpectMsg<ListenerActor.ListeningStarted>(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(9000, listening.BoundPort);
        Assert.NotNull(listening.Handle);
        Assert.NotNull(listening.Handle.AcceptSwitch);
        Assert.NotNull(listening.Handle.DrainSwitch);
        Assert.NotNull(listening.Handle.CompletionTask);
    }
}
