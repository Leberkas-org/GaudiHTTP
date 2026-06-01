using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class ConnectionFlowFactorySpec : StreamTestBase
{
    private sealed class FakeApplication(Func<IFeatureCollection, Task> handler)
        : IHttpApplication<IFeatureCollection>
    {
        public IFeatureCollection CreateContext(IFeatureCollection contextFeatures) => contextFeatures;
        public Task ProcessRequestAsync(IFeatureCollection context) => handler(context);

        public void DisposeContext(IFeatureCollection context, Exception? exception)
        {
        }
    }

    private static IFeatureCollection Request(string protocol = "HTTP/2")
    {
        var fc = new FeatureCollection();
        fc.Set<IHttpRequestFeature>(new TurboHttpRequestFeature { Protocol = protocol });
        fc.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        fc.Set<IHttpResponseBodyFeature>(new TurboHttpResponseBodyFeature());
        return fc;
    }

    private PipelineHandles MaterializePipeline(FakeApplication app, TurboServerOptions options)
    {
        var dispatcher = new FairShareDispatcher(
            options.Limits.MaxConcurrentRequests,
            options.Limits.MinRequestGuarantee);

        var pipelineKillSwitch = KillSwitches.Shared("test-pipeline");

        var parallelism = options.Limits.MaxConcurrentRequests > 0
            ? options.Limits.MaxConcurrentRequests
            : int.MaxValue;

        var bridgeStage = new ApplicationBridgeStage<IFeatureCollection>(
            app, parallelism, options.HandlerTimeout, options.HandlerGracePeriod);

        var responseHub = new ResponseDispatcherHub();

        var (requestSink, responseDispatcher) = MergeHub.Source<IFeatureCollection>(perProducerBufferSize: 64)
            .Via(pipelineKillSwitch.Flow<IFeatureCollection>())
            .Via(Flow.FromGraph(bridgeStage))
            .ToMaterialized(responseHub, Keep.Both)
            .Run(Materializer);

        return new PipelineHandles(requestSink, responseDispatcher, dispatcher);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionFlowFactory_should_dispatch_through_shared_pipeline()
    {
        var app = new FakeApplication(_ => Task.CompletedTask);
        var options = new TurboServerOptions
        {
            Limits =
            {
                MaxConcurrentRequests = 100
            }
        };
        var handles = MaterializePipeline(app, options);

        var flow = ConnectionFlowFactory.Create(1, handles, unordered: true);

        var (up, down) = this.SourceProbe<IFeatureCollection>()
            .Via(flow)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        down.Request(1);
        up.SendNext(Request(), TestContext.Current.CancellationToken);
        var result = down.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.NotNull(result.Get<IConnectionTagFeature>());
        Assert.Equal(1, result.Get<IConnectionTagFeature>()!.ConnectionId);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionFlowFactory_should_route_responses_to_correct_connection()
    {
        var app = new FakeApplication(_ => Task.CompletedTask);
        var options = new TurboServerOptions
        {
            Limits =
            {
                MaxConcurrentRequests = 100
            }
        };
        var handles = MaterializePipeline(app, options);

        var flow1 = ConnectionFlowFactory.Create(1, handles, unordered: true);
        var flow2 = ConnectionFlowFactory.Create(2, handles, unordered: true);

        var (up1, down1) = this.SourceProbe<IFeatureCollection>()
            .Via(flow1)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        var (up2, down2) = this.SourceProbe<IFeatureCollection>()
            .Via(flow2)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        down1.Request(1);
        down2.Request(1);

        up1.SendNext(Request(), TestContext.Current.CancellationToken);
        up2.SendNext(Request(), TestContext.Current.CancellationToken);

        var r1 = down1.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var r2 = down2.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.Equal(1, r1.Get<IConnectionTagFeature>()!.ConnectionId);
        Assert.Equal(2, r2.Get<IConnectionTagFeature>()!.ConnectionId);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionFlowFactory_should_tag_requests_with_monotonic_sequence()
    {
        var app = new FakeApplication(_ => Task.CompletedTask);
        var options = new TurboServerOptions
        {
            Limits =
            {
                MaxConcurrentRequests = 100
            }
        };
        var handles = MaterializePipeline(app, options);

        var flow = ConnectionFlowFactory.Create(1, handles, unordered: true);

        var (up, down) = this.SourceProbe<IFeatureCollection>()
            .Via(flow)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        down.Request(3);
        up.SendNext(Request(), TestContext.Current.CancellationToken);
        up.SendNext(Request(), TestContext.Current.CancellationToken);
        up.SendNext(Request(), TestContext.Current.CancellationToken);

        var r1 = down.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var r2 = down.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        var r3 = down.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        var seq1 = r1.Get<IConnectionTagFeature>()?.RequestSequence;
        var seq2 = r2.Get<IConnectionTagFeature>()?.RequestSequence;
        var seq3 = r3.Get<IConnectionTagFeature>()?.RequestSequence;

        Assert.Equal(0, seq1);
        Assert.Equal(1, seq2);
        Assert.Equal(2, seq3);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionFlowFactory_should_release_fairshare_slot_on_response()
    {
        var app = new FakeApplication(_ => Task.CompletedTask);
        var options = new TurboServerOptions
        {
            Limits =
            {
                MaxConcurrentRequests = 1
            }
        };
        var handles = MaterializePipeline(app, options);

        var flow = ConnectionFlowFactory.Create(1, handles, unordered: true);

        var (up, down) = this.SourceProbe<IFeatureCollection>()
            .Via(flow)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        down.Request(2);
        up.SendNext(Request(), TestContext.Current.CancellationToken);
        down.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.Equal(0, handles.Dispatcher.GetConnectionInFlight(1));
    }

    [Fact(Timeout = 10000)]
    public void ConnectionFlowFactory_should_work_with_bidiflow_join()
    {
        var app = new FakeApplication(_ => Task.CompletedTask);
        var options = new TurboServerOptions
        {
            Limits =
            {
                MaxConcurrentRequests = 100
            }
        };
        var handles = MaterializePipeline(app, options);

        var connectionFlow = ConnectionFlowFactory.Create(1, handles, unordered: true);

        var passThroughBidi = BidiFlow.FromFlows(
            Flow.Create<IFeatureCollection>(),
            Flow.Create<IFeatureCollection>());

        var composed = passThroughBidi.Join(connectionFlow);

        var (up, down) = this.SourceProbe<IFeatureCollection>()
            .Via(composed)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        down.Request(1);
        up.SendNext(Request(), TestContext.Current.CancellationToken);
        var result = down.ExpectNext(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotNull(result.Get<IConnectionTagFeature>());
    }
}