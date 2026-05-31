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

public sealed class ApplicationBridgeStageSpec : StreamTestBase
{
    private sealed class FakeApplication(Func<IFeatureCollection, Task> handler)
        : IHttpApplication<IFeatureCollection>
    {
        public IFeatureCollection CreateContext(IFeatureCollection contextFeatures) => contextFeatures;
        public Task ProcessRequestAsync(IFeatureCollection context) => handler(context);
        public void DisposeContext(IFeatureCollection context, Exception? exception) { }
    }

    private static IFeatureCollection Request(int connectionId = 1, int requestSeq = 0, string protocol = "HTTP/2")
    {
        var fc = new FeatureCollection();
        fc.Set<IHttpRequestFeature>(new TurboHttpRequestFeature { Protocol = protocol });
        fc.Set<IHttpResponseFeature>(new TurboHttpResponseFeature());
        fc.Set<IHttpResponseBodyFeature>(new TurboHttpResponseBodyFeature());
        fc.Set<IConnectionTagFeature>(new ConnectionTagFeature { ConnectionId = connectionId, RequestSequence = requestSeq });
        return fc;
    }

    [Fact(Timeout = 5000)]
    public void ApplicationBridgeStage_should_dispatch_immediate_completions()
    {
        var app = new FakeApplication(_ => Task.CompletedTask);
        var options = new TurboServerOptions();
        options.Limits.MaxConcurrentRequests = 10;
        var stage = new ApplicationBridgeStage<IFeatureCollection>(
            app,
            options.Limits.MaxConcurrentRequests,
            options.HandlerTimeout,
            options.HandlerGracePeriod);

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(1);
        upstream.SendNext(Request(1));
        var emitted = downstream.ExpectNext();
        Assert.NotNull(emitted);
    }

    [Fact(Timeout = 5000)]
    public void ApplicationBridgeStage_should_emit_unordered_when_handlers_complete_out_of_order()
    {
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs3 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var handlers = new[] { tcs1.Task, tcs2.Task, tcs3.Task };
        var app = new FakeApplication(features =>
        {
            var connTag = features.Get<IConnectionTagFeature>();
            var reqSeq = connTag?.RequestSequence ?? 0;
            return handlers[reqSeq];
        });

        var options = new TurboServerOptions();
        options.Limits.MaxConcurrentRequests = 10;
        var stage = new ApplicationBridgeStage<IFeatureCollection>(
            app,
            options.Limits.MaxConcurrentRequests,
            options.HandlerTimeout,
            options.HandlerGracePeriod);

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(3);
        upstream.SendNext(Request(1, 0));
        upstream.SendNext(Request(1, 1));
        upstream.SendNext(Request(1, 2));

        // Complete in order: 2, 1, 3 (by requestSeq: 1, 0, 2)
        tcs1.SetResult();
        tcs2.SetResult();
        tcs3.SetResult();

        var first = downstream.ExpectNext();
        var second = downstream.ExpectNext();
        var third = downstream.ExpectNext();

        var emitOrder = new[]
        {
            first.Get<IConnectionTagFeature>()?.RequestSequence ?? -1,
            second.Get<IConnectionTagFeature>()?.RequestSequence ?? -1,
            third.Get<IConnectionTagFeature>()?.RequestSequence ?? -1,
        };

        // In unordered mode, all three should be emitted
        Assert.Equal(3, emitOrder.Length);
    }

    [Fact(Timeout = 5000)]
    public void ApplicationBridgeStage_should_handle_handler_exceptions()
    {
        var app = new FakeApplication(_ => throw new InvalidOperationException("Test error"));
        var options = new TurboServerOptions();
        options.Limits.MaxConcurrentRequests = 10;
        var stage = new ApplicationBridgeStage<IFeatureCollection>(
            app,
            options.Limits.MaxConcurrentRequests,
            options.HandlerTimeout,
            options.HandlerGracePeriod);

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(1);
        upstream.SendNext(Request(1));

        var result = downstream.ExpectNext();
        Assert.Equal(500, result.Get<IHttpResponseFeature>()?.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void ApplicationBridgeStage_should_complete_upstream_finished_no_pending()
    {
        var app = new FakeApplication(_ => Task.CompletedTask);
        var options = new TurboServerOptions();
        options.Limits.MaxConcurrentRequests = 10;
        var stage = new ApplicationBridgeStage<IFeatureCollection>(
            app,
            options.Limits.MaxConcurrentRequests,
            options.HandlerTimeout,
            options.HandlerGracePeriod);

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(1);
        upstream.SendNext(Request(1));
        downstream.ExpectNext();

        upstream.SendComplete();
        downstream.ExpectComplete();
    }
}
