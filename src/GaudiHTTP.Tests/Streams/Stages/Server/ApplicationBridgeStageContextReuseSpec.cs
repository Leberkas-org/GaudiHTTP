using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Abstractions;
using Microsoft.AspNetCore.Http.Features;
using GaudiHTTP.Pooling;
using GaudiHTTP.Server;
using GaudiHTTP.Server.Context.Features;
using GaudiHTTP.Streams.Stages.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Streams.Stages.Server;

public sealed class ApplicationBridgeStageContextReuseSpec : StreamTestBase
{
    private readonly ConnectionPoolContext _pool = new();

    // Mirrors how Microsoft.AspNetCore.Hosting.HostingApplication caches its context: it checks the
    // feature collection for IHostContextContainer<TContext> and reuses HostContext when present.
    private sealed class ReusableContext
    {
        public int CreateCount;
    }

    private sealed class HostLikeApplication(Func<ReusableContext, Task>? handler = null)
        : IHttpApplication<ReusableContext>
    {
        public int FreshAllocations;

        public ReusableContext CreateContext(IFeatureCollection contextFeatures)
        {
            if (contextFeatures is IHostContextContainer<ReusableContext> container)
            {
                var ctx = container.HostContext;
                if (ctx is null)
                {
                    ctx = container.HostContext = new ReusableContext();
                    FreshAllocations++;
                }

                ctx.CreateCount++;
                return ctx;
            }

            FreshAllocations++;
            return new ReusableContext { CreateCount = 1 };
        }

        public Task ProcessRequestAsync(ReusableContext context) => handler?.Invoke(context) ?? Task.CompletedTask;

        public void DisposeContext(ReusableContext context, Exception? exception) { }
    }

    private static ApplicationBridgeStage<ReusableContext> CreateStage(IHttpApplication<ReusableContext> app)
        => new(app, 10, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

    private IFeatureCollection Request()
        => FeatureCollectionFactory.Create(_pool, new GaudiHttpRequestFeature { Protocol = "HTTP/1.1" }, hasBody: false);

    [Fact(Timeout = 5000)]
    public void CreateContext_should_reuse_host_context_across_requests_on_pooled_collection()
    {
        var app = new HostLikeApplication();
        var stage = CreateStage(app);

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        // First request rents a fresh collection, completes, and is returned to the pool.
        downstream.Request(1);
        var first = Request();
        upstream.SendNext(first, TestContext.Current.CancellationToken);
        downstream.ExpectNext(TestContext.Current.CancellationToken);
        FeatureCollectionFactory.Return(_pool, first);

        // Second request re-rents the SAME pooled collection (and thus its cached host context wrapper).
        downstream.Request(1);
        var second = Request();
        Assert.Same(first, second);
        upstream.SendNext(second, TestContext.Current.CancellationToken);
        downstream.ExpectNext(TestContext.Current.CancellationToken);

        // The host context must have been allocated exactly once despite two CreateContext calls.
        Assert.Equal(1, app.FreshAllocations);
    }

    [Fact(Timeout = 5000)]
    public void CreateContext_should_allocate_distinct_contexts_for_distinct_collections()
    {
        var app = new HostLikeApplication();
        var stage = CreateStage(app);

        var (upstream, downstream) = this.SourceProbe<IFeatureCollection>()
            .Via(stage)
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        // Two distinct collections (e.g. two concurrent H2 streams) must not share a host context.
        downstream.Request(2);
        var a = Request();
        var b = Request();
        Assert.NotSame(a, b);
        upstream.SendNext(a, TestContext.Current.CancellationToken);
        upstream.SendNext(b, TestContext.Current.CancellationToken);
        downstream.ExpectNext(TestContext.Current.CancellationToken);
        downstream.ExpectNext(TestContext.Current.CancellationToken);

        Assert.Equal(2, app.FreshAllocations);
    }
}
