using Akka.Streams.Dsl;
using TurboHTTP.Server;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class MiddlewarePipelineStageSpec : StreamTestBase
{
    private TurboHttpContext CreateTestContext()
    {
        return ServerTestContext.Request()
            .Get("/test")
            .Materializer(Materializer)
            .Build();
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_pass_context_through_when_no_middleware()
    {
        TurboRequestDelegate pipeline = _ => Task.CompletedTask;
        var stage = new MiddlewarePipelineStage(pipeline);

        var ctx = CreateTestContext();
        var result = await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Same(ctx, result);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_execute_synchronous_middleware()
    {
        var called = false;
        TurboRequestDelegate pipeline = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };
        var stage = new MiddlewarePipelineStage(pipeline);

        var ctx = CreateTestContext();
        await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.True(called);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_execute_async_middleware()
    {
        var called = false;
        TurboRequestDelegate pipeline = async _ =>
        {
            await Task.Delay(10);
            called = true;
        };
        var stage = new MiddlewarePipelineStage(pipeline);

        var ctx = CreateTestContext();
        await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.True(called);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_set_500_on_middleware_failure()
    {
        TurboRequestDelegate pipeline = _ => throw new InvalidOperationException("boom");
        var stage = new MiddlewarePipelineStage(pipeline);

        var ctx = CreateTestContext();
        var result = await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Equal(500, result.Response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_set_500_on_async_middleware_failure()
    {
        TurboRequestDelegate pipeline = async _ =>
        {
            await Task.Delay(10);
            throw new InvalidOperationException("boom");
        };
        var stage = new MiddlewarePipelineStage(pipeline);

        var ctx = CreateTestContext();
        var result = await Source.Single(ctx)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.First<TurboHttpContext>(), Materializer);

        Assert.Equal(500, result.Response.StatusCode);
    }
}
