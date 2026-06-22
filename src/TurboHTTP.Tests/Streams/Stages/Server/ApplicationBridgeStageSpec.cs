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

    private static ApplicationBridgeStage<IFeatureCollection> CreateStage(
        IHttpApplication<IFeatureCollection> app,
        TurboServerOptions? options = null)
    {
        options ??= new TurboServerOptions();
        return new ApplicationBridgeStage<IFeatureCollection>(
            app, 10, options.HandlerTimeout, options.HandlerGracePeriod);
    }

    [Fact(Timeout = 5000)]
    public void ApplicationBridgeStage_should_dispatch_immediate_completions()
    {
        var app = new FakeApplication(_ => Task.CompletedTask);
        var stage = CreateStage(app);

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(1);
        upstream.SendNext(Request(), TestContext.Current.CancellationToken);
        var emitted = downstream.ExpectNext(TestContext.Current.CancellationToken);
        Assert.NotNull(emitted);
    }

    [Fact(Timeout = 5000)]
    public void ApplicationBridgeStage_should_emit_all_when_handlers_complete_out_of_order()
    {
        var tcs1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs3 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var handlerQueue = new Queue<Task>([tcs1.Task, tcs2.Task, tcs3.Task]);
        var app = new FakeApplication(_ => handlerQueue.Dequeue());
        var stage = CreateStage(app);

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(3);
        upstream.SendNext(Request(), TestContext.Current.CancellationToken);
        upstream.SendNext(Request(), TestContext.Current.CancellationToken);
        upstream.SendNext(Request(), TestContext.Current.CancellationToken);

        tcs2.SetResult();
        tcs1.SetResult();
        tcs3.SetResult();

        downstream.ExpectNext(TestContext.Current.CancellationToken);
        downstream.ExpectNext(TestContext.Current.CancellationToken);
        downstream.ExpectNext(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public void ApplicationBridgeStage_should_not_double_emit_when_handler_times_out_after_headers()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = new FakeApplication(async features =>
        {
            features.Get<IHttpResponseFeature>()!.StatusCode = 200;
            // Commit headers so WhenHeadersReady completes and the response is emitted, then hang
            // past the handler timeout. The hard timeout must NOT re-emit the already-sent response.
            await features.Get<IHttpResponseBodyFeature>()!.StartAsync();
            await release.Task;
        });

        var stage = new ApplicationBridgeStage<IFeatureCollection>(
            app, 1, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(10);
        upstream.SendNext(Request(), TestContext.Current.CancellationToken);

        downstream.ExpectNext(TestContext.Current.CancellationToken);
        downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(600), TestContext.Current.CancellationToken);

        release.SetResult();
    }

    [Fact(Timeout = 5000)]
    public void ApplicationBridgeStage_should_handle_handler_exceptions()
    {
        var app = new FakeApplication(_ => throw new InvalidOperationException("Test error"));
        var stage = CreateStage(app);

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(1);
        upstream.SendNext(Request(), TestContext.Current.CancellationToken);

        var result = downstream.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal(500, result.Get<IHttpResponseFeature>()?.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public void ApplicationBridgeStage_should_complete_upstream_finished_no_pending()
    {
        var app = new FakeApplication(_ => Task.CompletedTask);
        var stage = CreateStage(app);

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(1);
        upstream.SendNext(Request(), TestContext.Current.CancellationToken);
        downstream.ExpectNext(TestContext.Current.CancellationToken);

        upstream.SendComplete(TestContext.Current.CancellationToken);
        downstream.ExpectComplete(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task Buffered_async_handler_should_not_create_pipe()
    {
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = new FakeApplication(async features =>
        {
            handlerEntered.SetResult();
            await release.Task;
            var body = features.Get<IHttpResponseBodyFeature>()!;
            var writer = body.Writer;
            var mem = writer.GetMemory(2 * 1024);
            new byte[2 * 1024].CopyTo(mem);
            writer.Advance(2 * 1024);
            writer.Complete();
        });

        var stage = CreateStage(app);
        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(1);
        upstream.SendNext(Request(), TestContext.Current.CancellationToken);

        await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);
        release.SetResult();

        var emitted = downstream.ExpectNext(TestContext.Current.CancellationToken);
        var bodyFeature = emitted.Get<IHttpResponseBodyFeature>() as TurboHttpResponseBodyFeature;
        Assert.NotNull(bodyFeature);
        Assert.False(bodyFeature!.HasPipe,
            "Buffered async handler (no FlushAsync) should not create a Pipe");
    }

    [Fact(Timeout = 5000)]
    public async Task Streaming_async_handler_should_create_pipe_on_flush()
    {
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = new FakeApplication(async features =>
        {
            handlerEntered.SetResult();
            var body = features.Get<IHttpResponseBodyFeature>()!;
            await body.Writer.WriteAsync(new byte[512], TestContext.Current.CancellationToken);
            await body.Writer.FlushAsync(TestContext.Current.CancellationToken);
            await release.Task;
            body.Writer.Complete();
        });

        var stage = CreateStage(app);
        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        downstream.Request(1);
        upstream.SendNext(Request(), TestContext.Current.CancellationToken);

        await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        var emitted = downstream.ExpectNext(TestContext.Current.CancellationToken);
        var bodyFeature = emitted.Get<IHttpResponseBodyFeature>() as TurboHttpResponseBodyFeature;
        Assert.NotNull(bodyFeature);
        Assert.True(bodyFeature!.HasPipe,
            "Streaming async handler (explicit FlushAsync) should create a Pipe");

        release.SetResult();
    }
}
