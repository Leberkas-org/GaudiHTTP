using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class FairShareAdmissionStageSpec : StreamTestBase
{
    [Fact(Timeout = 5000)]
    public void FairShareAdmissionStage_should_pass_through_when_slot_available()
    {
        var dispatcher = new FairShareDispatcher(totalLimit: 100, minGuarantee: 10);
        dispatcher.RegisterConnection(1);

        var stage = new FairShareAdmissionStage(1, dispatcher);
        var (up, down) = this.SourceProbe<IFeatureCollection>()
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        down.Request(1);
        var fc = new FeatureCollection();
        up.SendNext(fc);
        Assert.Same(fc, down.ExpectNext());
    }

    [Fact(Timeout = 5000)]
    public void FairShareAdmissionStage_should_stash_when_slot_rejected_and_resume_on_release()
    {
        var dispatcher = new FairShareDispatcher(totalLimit: 1, minGuarantee: 1);
        dispatcher.RegisterConnection(1);

        var stage = new FairShareAdmissionStage(1, dispatcher);
        var (up, down) = this.SourceProbe<IFeatureCollection>()
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        down.Request(2);

        var fc1 = new FeatureCollection();
        var fc2 = new FeatureCollection();
        up.SendNext(fc1);
        Assert.Same(fc1, down.ExpectNext());

        up.SendNext(fc2);
        down.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        dispatcher.Release(1);
        Assert.Same(fc2, down.ExpectNext(TimeSpan.FromSeconds(3)));
    }

    [Fact(Timeout = 5000)]
    public void FairShareAdmissionStage_should_unregister_connection_on_stage_stop()
    {
        var dispatcher = new FairShareDispatcher(totalLimit: 100, minGuarantee: 10);
        dispatcher.RegisterConnection(1);
        dispatcher.TryAcquire(1);
        Assert.Equal(1, dispatcher.GetConnectionInFlight(1));

        var stage = new FairShareAdmissionStage(1, dispatcher);
        var (up, down) = this.SourceProbe<IFeatureCollection>()
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        up.SendComplete();
        down.Request(1);
        down.ExpectComplete();

        Assert.Equal(0, dispatcher.GetConnectionInFlight(1));
    }
}
