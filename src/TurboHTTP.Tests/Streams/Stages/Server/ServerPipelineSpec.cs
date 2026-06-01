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

public sealed class ServerPipelineSpec : StreamTestBase
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

    private ServerPipeline MaterializePipeline(FakeApplication app, TurboServerOptions options)
    {
        var pipelineKillSwitch = KillSwitches.Shared("test-pipeline");

        var parallelism = options.Limits.MaxConcurrentRequests > 0
            ? options.Limits.MaxConcurrentRequests
            : int.MaxValue;

        var bridgeStage = new ApplicationBridgeStage<IFeatureCollection>(
            app, parallelism, options.HandlerTimeout, options.HandlerGracePeriod);

        return ServerPipeline.Materialize(
            Flow.FromGraph(bridgeStage), options, pipelineKillSwitch, Materializer, Sys);
    }

    [Fact(Timeout = 5000)]
    public void ServerPipeline_should_dispatch_through_shared_pipeline()
    {
        var app = new FakeApplication(_ => Task.CompletedTask);
        var options = new TurboServerOptions { Limits = { MaxConcurrentRequests = 100 } };
        var pipeline = MaterializePipeline(app, options);

        var flow = pipeline.CreateConnectionFlow(1, unordered: true);

        var (up, down) = this.SourceProbe<IFeatureCollection>()
            .Via(flow)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        down.Request(1);
        up.SendNext(Request(), TestContext.Current.CancellationToken);
        var result = down.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(1, result.Get<IConnectionTagFeature>()!.ConnectionId);
    }

    [Fact(Timeout = 5000)]
    public void ServerPipeline_should_route_responses_to_correct_connection()
    {
        var app = new FakeApplication(_ => Task.CompletedTask);
        var options = new TurboServerOptions { Limits = { MaxConcurrentRequests = 100 } };
        var pipeline = MaterializePipeline(app, options);

        var flow1 = pipeline.CreateConnectionFlow(1, unordered: true);
        var flow2 = pipeline.CreateConnectionFlow(2, unordered: true);

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
    public void ServerPipeline_should_tag_requests_with_monotonic_sequence()
    {
        var app = new FakeApplication(_ => Task.CompletedTask);
        var options = new TurboServerOptions { Limits = { MaxConcurrentRequests = 100 } };
        var pipeline = MaterializePipeline(app, options);

        var flow = pipeline.CreateConnectionFlow(1, unordered: true);

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

        Assert.Equal(0, r1.Get<IConnectionTagFeature>()!.RequestSequence);
        Assert.Equal(1, r2.Get<IConnectionTagFeature>()!.RequestSequence);
        Assert.Equal(2, r3.Get<IConnectionTagFeature>()!.RequestSequence);
    }

    [Fact(Timeout = 5000)]
    public void ServerPipeline_should_release_fairshare_slot_on_response()
    {
        var app = new FakeApplication(_ => Task.CompletedTask);
        var options = new TurboServerOptions { Limits = { MaxConcurrentRequests = 1 } };
        var pipeline = MaterializePipeline(app, options);

        var flow = pipeline.CreateConnectionFlow(1, unordered: true);

        var (up, down) = this.SourceProbe<IFeatureCollection>()
            .Via(flow)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        down.Request(2);
        up.SendNext(Request(), TestContext.Current.CancellationToken);
        down.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        up.SendNext(Request(), TestContext.Current.CancellationToken);
        down.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public void ServerPipeline_should_work_with_bidiflow_join()
    {
        var app = new FakeApplication(_ => Task.CompletedTask);
        var options = new TurboServerOptions { Limits = { MaxConcurrentRequests = 100 } };
        var pipeline = MaterializePipeline(app, options);

        var connectionFlow = pipeline.CreateConnectionFlow(1, unordered: true);
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
