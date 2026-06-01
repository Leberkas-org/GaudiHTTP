using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class ResponseReorderStageSpec : StreamTestBase
{
    private static IFeatureCollection Tagged(int connectionId, int seq)
    {
        var fc = new FeatureCollection();
        fc.Set<IConnectionTagFeature>(new ConnectionTagFeature
        {
            ConnectionId = connectionId,
            RequestSequence = seq
        });
        return fc;
    }

    [Fact(Timeout = 5000)]
    public void ResponseReorderStage_should_emit_in_order_for_ordered_mode()
    {
        var stage = new ResponseReorderStage(unordered: false);
        var (up, down) = this.SourceProbe<IFeatureCollection>()
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        down.Request(3);

        var r0 = Tagged(1, 0);
        var r1 = Tagged(1, 1);
        var r2 = Tagged(1, 2);

        // Send out of order: 2, 0, 1
        up.SendNext(r2, TestContext.Current.CancellationToken);
        up.SendNext(r0, TestContext.Current.CancellationToken);
        up.SendNext(r1, TestContext.Current.CancellationToken);

        // Should emit in order: 0, 1, 2
        Assert.Same(r0, down.ExpectNext(TestContext.Current.CancellationToken));
        Assert.Same(r1, down.ExpectNext(TestContext.Current.CancellationToken));
        Assert.Same(r2, down.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public void ResponseReorderStage_should_passthrough_for_unordered_mode()
    {
        var stage = new ResponseReorderStage(unordered: true);
        var (up, down) = this.SourceProbe<IFeatureCollection>()
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        down.Request(3);

        var r0 = Tagged(1, 0);
        var r1 = Tagged(1, 1);
        var r2 = Tagged(1, 2);

        // Send out of order: 2, 0, 1
        up.SendNext(r2, TestContext.Current.CancellationToken);
        Assert.Same(r2, down.ExpectNext(TestContext.Current.CancellationToken));
        up.SendNext(r0, TestContext.Current.CancellationToken);
        Assert.Same(r0, down.ExpectNext(TestContext.Current.CancellationToken));
        up.SendNext(r1, TestContext.Current.CancellationToken);
        Assert.Same(r1, down.ExpectNext(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public void ResponseReorderStage_should_complete_after_all_buffered_emitted()
    {
        var stage = new ResponseReorderStage(unordered: false);
        var (up, down) = this.SourceProbe<IFeatureCollection>()
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        down.Request(2);

        var r0 = Tagged(1, 0);
        var r1 = Tagged(1, 1);

        up.SendNext(r1, TestContext.Current.CancellationToken);
        up.SendNext(r0, TestContext.Current.CancellationToken);
        up.SendComplete(TestContext.Current.CancellationToken);

        Assert.Same(r0, down.ExpectNext(TestContext.Current.CancellationToken));
        Assert.Same(r1, down.ExpectNext(TestContext.Current.CancellationToken));
        down.ExpectComplete(TestContext.Current.CancellationToken);
    }
}