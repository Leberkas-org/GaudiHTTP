using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class FairShareAdmissionStageSpec : StreamTestBase
{
    private IActorRef CreateCoordinator(int totalLimit, int minGuarantee)
        => Sys.ActorOf(FairShareCoordinator.Props(totalLimit, minGuarantee));

    [Fact(Timeout = 5000)]
    public void FairShareAdmissionStage_should_pass_through_when_slot_available()
    {
        var coordinator = CreateCoordinator(totalLimit: 100, minGuarantee: 10);
        coordinator.Tell(new FairShareCoordinator.Register(1));

        var stage = new FairShareAdmissionStage(1, coordinator);
        var (up, down) = this.SourceProbe<IFeatureCollection>()
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        down.Request(1);
        var fc = new FeatureCollection();
        up.SendNext(fc, TestContext.Current.CancellationToken);
        Assert.Same(fc, down.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public void FairShareAdmissionStage_should_stash_when_no_slot_and_resume_on_release()
    {
        var coordinator = CreateCoordinator(totalLimit: 1, minGuarantee: 1);
        coordinator.Tell(new FairShareCoordinator.Register(1));

        var stage = new FairShareAdmissionStage(1, coordinator);
        var (up, down) = this.SourceProbe<IFeatureCollection>()
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        down.Request(2);

        var fc1 = new FeatureCollection();
        var fc2 = new FeatureCollection();
        up.SendNext(fc1, TestContext.Current.CancellationToken);
        Assert.Same(fc1, down.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));

        up.SendNext(fc2, TestContext.Current.CancellationToken);
        down.ExpectNoMsg(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        coordinator.Tell(new FairShareCoordinator.Release(1));
        Assert.Same(fc2, down.ExpectNext(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public void FairShareAdmissionStage_should_unregister_on_stop()
    {
        var coordinator = CreateCoordinator(totalLimit: 100, minGuarantee: 10);

        var stage = new FairShareAdmissionStage(1, coordinator);
        var (up, down) = this.SourceProbe<IFeatureCollection>()
            .Via(Flow.FromGraph(stage))
            .ToMaterialized(this.SinkProbe<IFeatureCollection>(), Keep.Both)
            .Run(Materializer);

        up.SendComplete(TestContext.Current.CancellationToken);
        down.Request(1);
        down.ExpectComplete(TestContext.Current.CancellationToken);

        coordinator.Tell(new FairShareCoordinator.Acquire(1, TestActor));
        ExpectNoMsg(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
    }
}
