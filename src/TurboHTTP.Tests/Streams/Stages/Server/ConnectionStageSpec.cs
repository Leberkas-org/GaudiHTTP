using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using System.Net;
using TurboHTTP.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class ConnectionStageSpec : StreamTestBase
{
    private ServerPipeline CreatePassthroughPipeline()
    {
        var options = new TurboServerOptions { Limits = { MaxConcurrentRequests = 0 } };
        var killSwitch = KillSwitches.Shared("connstage-test-pipeline");
        return ServerPipeline.Materialize(
            Flow.Create<IFeatureCollection>(), options, killSwitch, Materializer);
    }

    private sealed class PassthroughEngine : IServerProtocolEngine
    {
        public Version ProtocolVersion => new(1, 1);

        public BidiFlow<ITransportInbound, IFeatureCollection, IFeatureCollection, ITransportOutbound, NotUsed>
            CreateFlow(
                IServiceProvider? services = null)
        {
            var top = Flow.Create<ITransportInbound>()
                .Select(_ => (IFeatureCollection)new FeatureCollection());
            var bottom = Flow.Create<IFeatureCollection>()
                .Select(_ => new DisconnectTransport(DisconnectReason.Graceful) as ITransportOutbound);
            return BidiFlow.FromFlows(top, bottom);
        }
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
    public async Task ConnectionStage_should_complete_when_inlet_closes_with_no_connections()
    {
        var options = new TurboServerOptions();
        var pipeline = CreatePassthroughPipeline();
        var engine = new PassthroughEngine();
        var completionTcs = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);

        var stage = new ConnectionStage(options, pipeline, engine);
        var flow = stage.CreateFlow(completionTcs);

        _ = Source.Empty<Flow<ITransportOutbound, ITransportInbound, NotUsed>>()
            .Via(flow)
            .RunWith(Sink.Ignore<NotUsed>(), Materializer);

        var result = await completionTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(Done.Instance, result);
    }

    [Fact(Timeout = 10000)]
    public async Task ConnectionStage_should_complete_after_connections_finish()
    {
        var options = new TurboServerOptions();
        var pipeline = CreatePassthroughPipeline();
        var engine = new PassthroughEngine();
        var completionTcs = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);

        var stage = new ConnectionStage(options, pipeline, engine);
        var flow = stage.CreateFlow(completionTcs);

        _ = Source.From([FakeConnectionFlow(), FakeConnectionFlow()])
            .Via(flow)
            .RunWith(Sink.Ignore<NotUsed>(), Materializer);

        var result = await completionTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(Done.Instance, result);
    }

    [Fact(Timeout = 10000)]
    public async Task ConnectionStage_should_drain_on_shared_kill_switch()
    {
        var options = new TurboServerOptions();
        var pipeline = CreatePassthroughPipeline();
        var engine = new PassthroughEngine();
        var drainSwitch = KillSwitches.Shared("test-drain");
        var completionTcs = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);

        var stage = new ConnectionStage(options, pipeline, engine, drainSwitch);
        var flow = stage.CreateFlow(completionTcs);

        _ = Source.From([HangingConnectionFlow(), HangingConnectionFlow()])
            .Via(flow)
            .RunWith(Sink.Ignore<NotUsed>(), Materializer);

        await Task.Delay(500, TestContext.Current.CancellationToken);
        drainSwitch.Shutdown();

        var result = await completionTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(Done.Instance, result);
    }
}