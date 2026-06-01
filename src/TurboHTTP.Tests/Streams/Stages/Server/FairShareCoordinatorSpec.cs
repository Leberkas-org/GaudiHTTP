using Akka.Actor;
using Akka.TestKit.Xunit;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class FairShareCoordinatorSpec : TestKit
{
    private IActorRef CreateCoordinator(int totalLimit, int minGuarantee)
        => Sys.ActorOf(FairShareCoordinator.Props(totalLimit, minGuarantee));

    [Fact(Timeout = 5000)]
    public void FairShareCoordinator_should_grant_within_guarantee()
    {
        var coordinator = CreateCoordinator(totalLimit: 100, minGuarantee: 10);
        coordinator.Tell(new FairShareCoordinator.Register(1));
        coordinator.Tell(new FairShareCoordinator.Acquire(1, TestActor));

        ExpectMsg<FairShareCoordinator.Granted>(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public void FairShareCoordinator_should_grant_from_shared_pool_above_guarantee()
    {
        var coordinator = CreateCoordinator(totalLimit: 100, minGuarantee: 5);
        coordinator.Tell(new FairShareCoordinator.Register(1));

        for (var i = 0; i < 6; i++)
        {
            coordinator.Tell(new FairShareCoordinator.Acquire(1, TestActor));
            ExpectMsg<FairShareCoordinator.Granted>(cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Fact(Timeout = 5000)]
    public void FairShareCoordinator_should_queue_when_total_limit_reached_and_grant_on_release()
    {
        var coordinator = CreateCoordinator(totalLimit: 1, minGuarantee: 1);
        coordinator.Tell(new FairShareCoordinator.Register(1));

        coordinator.Tell(new FairShareCoordinator.Acquire(1, TestActor));
        ExpectMsg<FairShareCoordinator.Granted>(cancellationToken: TestContext.Current.CancellationToken);

        coordinator.Tell(new FairShareCoordinator.Acquire(1, TestActor));
        ExpectNoMsg(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        coordinator.Tell(new FairShareCoordinator.Release(1));
        ExpectMsg<FairShareCoordinator.Granted>(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public void FairShareCoordinator_should_degrade_guarantee_when_connections_exceed_budget()
    {
        var coordinator = CreateCoordinator(totalLimit: 10, minGuarantee: 5);
        coordinator.Tell(new FairShareCoordinator.Register(1));
        coordinator.Tell(new FairShareCoordinator.Register(2));
        coordinator.Tell(new FairShareCoordinator.Register(3));

        coordinator.Tell(new FairShareCoordinator.GetEffectiveGuarantee(TestActor));
        var reply = ExpectMsg<FairShareCoordinator.EffectiveGuaranteeReply>(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(3, reply.Value);
    }

    [Fact(Timeout = 5000)]
    public void FairShareCoordinator_should_restore_guarantee_after_unregister()
    {
        var coordinator = CreateCoordinator(totalLimit: 10, minGuarantee: 5);
        coordinator.Tell(new FairShareCoordinator.Register(1));
        coordinator.Tell(new FairShareCoordinator.Register(2));
        coordinator.Tell(new FairShareCoordinator.Register(3));
        coordinator.Tell(new FairShareCoordinator.Unregister(3));

        coordinator.Tell(new FairShareCoordinator.GetEffectiveGuarantee(TestActor));
        var reply = ExpectMsg<FairShareCoordinator.EffectiveGuaranteeReply>(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(5, reply.Value);
    }

    [Fact(Timeout = 5000)]
    public void FairShareCoordinator_should_always_grant_when_unlimited()
    {
        var coordinator = CreateCoordinator(totalLimit: 0, minGuarantee: 10);
        coordinator.Tell(new FairShareCoordinator.Register(1));

        for (var i = 0; i < 100; i++)
        {
            coordinator.Tell(new FairShareCoordinator.Acquire(1, TestActor));
            ExpectMsg<FairShareCoordinator.Granted>(cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Fact(Timeout = 5000)]
    public void FairShareCoordinator_should_be_fair_across_connections()
    {
        var coordinator = CreateCoordinator(totalLimit: 12, minGuarantee: 3);
        coordinator.Tell(new FairShareCoordinator.Register(1));
        coordinator.Tell(new FairShareCoordinator.Register(2));

        for (var i = 0; i < 3; i++)
        {
            coordinator.Tell(new FairShareCoordinator.Acquire(1, TestActor));
            ExpectMsg<FairShareCoordinator.Granted>(cancellationToken: TestContext.Current.CancellationToken);
            coordinator.Tell(new FairShareCoordinator.Acquire(2, TestActor));
            ExpectMsg<FairShareCoordinator.Granted>(cancellationToken: TestContext.Current.CancellationToken);
        }

        for (var i = 0; i < 6; i++)
        {
            coordinator.Tell(new FairShareCoordinator.Acquire(1, TestActor));
            ExpectMsg<FairShareCoordinator.Granted>(cancellationToken: TestContext.Current.CancellationToken);
        }

        coordinator.Tell(new FairShareCoordinator.Acquire(2, TestActor));
        ExpectNoMsg(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        coordinator.Tell(new FairShareCoordinator.Acquire(1, TestActor));
        ExpectNoMsg(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
    }
}
