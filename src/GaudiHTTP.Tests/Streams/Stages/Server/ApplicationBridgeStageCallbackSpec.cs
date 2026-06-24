using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Pooling;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class ApplicationBridgeStageCallbackSpec : StreamTestBase
{
    private readonly ConnectionPoolContext _pool = new();

    private sealed class CallbackTrackingApplication(Func<IFeatureCollection, Task> handler)
        : IHttpApplication<IFeatureCollection>
    {
        public IFeatureCollection CreateContext(IFeatureCollection contextFeatures) => contextFeatures;
        public Task ProcessRequestAsync(IFeatureCollection context) => handler(context);
        public void DisposeContext(IFeatureCollection context, Exception? exception) { }
    }

    private IFeatureCollection RequestWithCallbacks()
    {
        var requestFeature = new TurboHttpRequestFeature { Protocol = "HTTP/2" };
        return FeatureCollectionFactory.Create(_pool, requestFeature, hasBody: false);
    }

    private static ApplicationBridgeStage<IFeatureCollection> CreateStage(
        IHttpApplication<IFeatureCollection> app)
    {
        var options = new TurboServerOptions
        {
            HandlerTimeout = TimeSpan.FromSeconds(30),
            HandlerGracePeriod = TimeSpan.FromSeconds(5),
        };
        return new ApplicationBridgeStage<IFeatureCollection>(
            app,
            10,
            options.HandlerTimeout,
            options.HandlerGracePeriod);
    }

    [Fact(Timeout = 5000)]
    public void OnStarting_should_fire_when_handler_writes_response_body()
    {
        var onStartingCalled = false;

        var app = new CallbackTrackingApplication(features =>
        {
            var responseFeature = features.Get<IHttpResponseFeature>()!;
            responseFeature.OnStarting(_ =>
            {
                onStartingCalled = true;
                return Task.CompletedTask;
            }, null!);

            var bodyFeature = features.Get<IHttpResponseBodyFeature>()!;
            return bodyFeature.Writer.WriteAsync(new ReadOnlyMemory<byte>("hello"u8.ToArray())).AsTask();
        });

        var stage = CreateStage(app);

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(1);
        upstream.SendNext(RequestWithCallbacks(), TestContext.Current.CancellationToken);
        downstream.ExpectNext(TestContext.Current.CancellationToken);

        Assert.True(onStartingCalled);
    }

    [Fact(Timeout = 5000)]
    public void OnStarting_should_fire_when_handler_calls_StartAsync()
    {
        var onStartingCalled = false;

        var app = new CallbackTrackingApplication(async features =>
        {
            var responseFeature = features.Get<IHttpResponseFeature>()!;
            responseFeature.OnStarting(_ =>
            {
                onStartingCalled = true;
                return Task.CompletedTask;
            }, null!);

            var bodyFeature = features.Get<IHttpResponseBodyFeature>()!;
            await bodyFeature.StartAsync();
        });

        var stage = CreateStage(app);

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(1);
        upstream.SendNext(RequestWithCallbacks(), TestContext.Current.CancellationToken);
        downstream.ExpectNext(TestContext.Current.CancellationToken);

        Assert.True(onStartingCalled);
    }

    [Fact(Timeout = 5000)]
    public void OnStarting_should_allow_modifying_headers_before_flush()
    {
        var app = new CallbackTrackingApplication(features =>
        {
            var responseFeature = features.Get<IHttpResponseFeature>()!;
            responseFeature.OnStarting(_ =>
            {
                responseFeature.Headers["X-Added-By-Callback"] = "true";
                return Task.CompletedTask;
            }, null!);

            var bodyFeature = features.Get<IHttpResponseBodyFeature>()!;
            return bodyFeature.Writer.WriteAsync(new ReadOnlyMemory<byte>("data"u8.ToArray())).AsTask();
        });

        var stage = CreateStage(app);

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(1);
        upstream.SendNext(RequestWithCallbacks(), TestContext.Current.CancellationToken);
        var result = downstream.ExpectNext(TestContext.Current.CancellationToken);

        var headers = result.Get<IHttpResponseFeature>()!.Headers;
        Assert.Equal("true", headers["X-Added-By-Callback"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void OnCompleted_should_fire_when_handler_completes_successfully()
    {
        var onCompletedCalled = false;

        var app = new CallbackTrackingApplication(features =>
        {
            var responseFeature = features.Get<IHttpResponseFeature>()!;
            responseFeature.OnCompleted(_ =>
            {
                onCompletedCalled = true;
                return Task.CompletedTask;
            }, null!);

            return Task.CompletedTask;
        });

        var stage = CreateStage(app);

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(1);
        upstream.SendNext(RequestWithCallbacks(), TestContext.Current.CancellationToken);
        downstream.ExpectNext(TestContext.Current.CancellationToken);

        Assert.True(onCompletedCalled);
    }

    [Fact(Timeout = 5000)]
    public void OnCompleted_should_fire_when_handler_faults()
    {
        var onCompletedCalled = false;

        var app = new CallbackTrackingApplication(features =>
        {
            var responseFeature = features.Get<IHttpResponseFeature>()!;
            responseFeature.OnCompleted(_ =>
            {
                onCompletedCalled = true;
                return Task.CompletedTask;
            }, null!);

            throw new InvalidOperationException("handler error");
        });

        var stage = CreateStage(app);

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(1);
        upstream.SendNext(RequestWithCallbacks(), TestContext.Current.CancellationToken);
        var result = downstream.ExpectNext(TestContext.Current.CancellationToken);

        Assert.Equal(500, result.Get<IHttpResponseFeature>()!.StatusCode);
        Assert.True(onCompletedCalled);
    }
}
