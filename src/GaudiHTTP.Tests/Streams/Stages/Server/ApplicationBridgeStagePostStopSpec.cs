using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Streams.Stages.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Streams.Stages.Server;

public sealed class ApplicationBridgeStagePostStopSpec : StreamTestBase
{
    private sealed class TrackingApplication : IHttpApplication<IFeatureCollection>
    {
        public readonly List<(IFeatureCollection Context, Exception? Error)> DisposedContexts = [];

        public IFeatureCollection CreateContext(IFeatureCollection contextFeatures) => contextFeatures;

        public Task ProcessRequestAsync(IFeatureCollection context)
        {
            var tcs = context.Get<TestCompletionFeature>()?.Tcs;
            return tcs?.Task ?? Task.CompletedTask;
        }

        public void DisposeContext(IFeatureCollection context, Exception? exception)
        {
            DisposedContexts.Add((context, exception));
        }
    }

    private sealed class TestCompletionFeature
    {
        public TaskCompletionSource Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static IFeatureCollection RequestWithLifetime()
    {
        var fc = new FeatureCollection();
        fc.Set<IHttpRequestFeature>(new GaudiHttpRequestFeature { Protocol = "HTTP/2" });
        fc.Set<IHttpResponseFeature>(new GaudiHttpResponseFeature());
        fc.Set<IHttpResponseBodyFeature>(new GaudiHttpResponseBodyFeature());
        fc.Set<IHttpRequestLifetimeFeature>(new GaudiHttpRequestLifetimeFeature());
        fc.Set(new TestCompletionFeature());
        return fc;
    }

    private static (ApplicationBridgeStage<IFeatureCollection> Stage, TrackingApplication App) CreateStage()
    {
        var app = new TrackingApplication();
        var options = new TurboServerOptions
        {
            HandlerTimeout = TimeSpan.FromSeconds(30),
            HandlerGracePeriod = TimeSpan.FromSeconds(5),
        };
        var stage = new ApplicationBridgeStage<IFeatureCollection>(
            app,
            10,
            options.HandlerTimeout,
            options.HandlerGracePeriod);
        return (stage, app);
    }

    [Fact(Timeout = 5000)]
    public void PostStop_should_cancel_RequestAborted_for_inflight_requests()
    {
        var (stage, _) = CreateStage();

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(10);

        var request = RequestWithLifetime();
        var lifetime = request.Get<IHttpRequestLifetimeFeature>()!;
        var token = lifetime.RequestAborted;
        Assert.False(token.IsCancellationRequested);

        upstream.SendNext(request, TestContext.Current.CancellationToken);

        upstream.SendError(new Exception("connection dropped"), TestContext.Current.CancellationToken);
        downstream.ExpectError(TestContext.Current.CancellationToken);

        Assert.True(token.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public void PostStop_should_dispose_app_contexts_for_inflight_requests()
    {
        var (stage, app) = CreateStage();

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(10);

        var req1 = RequestWithLifetime();
        var req2 = RequestWithLifetime();
        upstream.SendNext(req1, TestContext.Current.CancellationToken);
        upstream.SendNext(req2, TestContext.Current.CancellationToken);

        Assert.Empty(app.DisposedContexts);

        upstream.SendError(new Exception("connection dropped"), TestContext.Current.CancellationToken);
        downstream.ExpectError(TestContext.Current.CancellationToken);

        Assert.Equal(2, app.DisposedContexts.Count);
    }

    [Fact(Timeout = 5000)]
    public void PostStop_should_cancel_handler_timeout_CTS_for_inflight_requests()
    {
        var (stage, _) = CreateStage();

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(10);

        var request = RequestWithLifetime();
        upstream.SendNext(request, TestContext.Current.CancellationToken);

        upstream.SendError(new Exception("connection dropped"), TestContext.Current.CancellationToken);
        downstream.ExpectError(TestContext.Current.CancellationToken);

        var lifetime = request.Get<IHttpRequestLifetimeFeature>()!;
        Assert.True(lifetime.RequestAborted.IsCancellationRequested);
    }
}
