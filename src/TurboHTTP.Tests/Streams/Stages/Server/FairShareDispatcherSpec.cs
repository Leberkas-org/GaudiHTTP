using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class FairShareDispatcherSpec
{
    [Fact(Timeout = 5000)]
    public void TryAcquire_should_succeed_within_guarantee()
    {
        var dispatcher = new FairShareDispatcher(totalLimit: 100, minGuarantee: 10);
        dispatcher.RegisterConnection(1);

        Assert.True(dispatcher.TryAcquire(connectionId: 1));
        Assert.Equal(1, dispatcher.GetConnectionInFlight(1));
    }

    [Fact(Timeout = 5000)]
    public void TryAcquire_should_use_shared_pool_above_guarantee()
    {
        var dispatcher = new FairShareDispatcher(totalLimit: 100, minGuarantee: 5);
        dispatcher.RegisterConnection(1);

        for (var i = 0; i < 5; i++)
        {
            Assert.True(dispatcher.TryAcquire(1));
        }

        Assert.True(dispatcher.TryAcquire(1));
        Assert.Equal(6, dispatcher.GetConnectionInFlight(1));
    }

    [Fact(Timeout = 5000)]
    public void TryAcquire_should_reject_when_total_limit_reached()
    {
        var dispatcher = new FairShareDispatcher(totalLimit: 3, minGuarantee: 2);
        dispatcher.RegisterConnection(1);

        Assert.True(dispatcher.TryAcquire(1));
        Assert.True(dispatcher.TryAcquire(1));
        Assert.True(dispatcher.TryAcquire(1));
        Assert.False(dispatcher.TryAcquire(1));
    }

    [Fact(Timeout = 5000)]
    public void Release_should_free_slot_for_reuse()
    {
        var dispatcher = new FairShareDispatcher(totalLimit: 1, minGuarantee: 1);
        dispatcher.RegisterConnection(1);

        Assert.True(dispatcher.TryAcquire(1));
        Assert.False(dispatcher.TryAcquire(1));

        dispatcher.Release(1);
        Assert.True(dispatcher.TryAcquire(1));
    }

    [Fact(Timeout = 5000)]
    public void SharedPool_should_shrink_when_connection_registers()
    {
        var dispatcher = new FairShareDispatcher(totalLimit: 20, minGuarantee: 5);
        dispatcher.RegisterConnection(1);

        for (var i = 0; i < 20; i++)
        {
            Assert.True(dispatcher.TryAcquire(1));
        }
        Assert.False(dispatcher.TryAcquire(1));

        for (var i = 0; i < 20; i++)
        {
            dispatcher.Release(1);
        }
        dispatcher.RegisterConnection(2);

        for (var i = 0; i < 15; i++)
        {
            Assert.True(dispatcher.TryAcquire(1));
        }
        Assert.False(dispatcher.TryAcquire(1));
    }

    [Fact(Timeout = 5000)]
    public void Guarantee_should_degrade_when_connections_exceed_budget()
    {
        var dispatcher = new FairShareDispatcher(totalLimit: 10, minGuarantee: 5);
        dispatcher.RegisterConnection(1);
        dispatcher.RegisterConnection(2);
        dispatcher.RegisterConnection(3);

        Assert.Equal(3, dispatcher.EffectiveGuarantee);
    }

    [Fact(Timeout = 5000)]
    public void UnregisterConnection_should_free_guarantee_budget()
    {
        var dispatcher = new FairShareDispatcher(totalLimit: 10, minGuarantee: 5);
        dispatcher.RegisterConnection(1);
        dispatcher.RegisterConnection(2);
        dispatcher.RegisterConnection(3);
        Assert.Equal(3, dispatcher.EffectiveGuarantee);

        dispatcher.UnregisterConnection(3);
        Assert.Equal(5, dispatcher.EffectiveGuarantee);
    }

    [Fact(Timeout = 5000)]
    public void TryAcquire_should_be_fair_across_connections()
    {
        var dispatcher = new FairShareDispatcher(totalLimit: 12, minGuarantee: 3);
        dispatcher.RegisterConnection(1);
        dispatcher.RegisterConnection(2);

        for (var i = 0; i < 3; i++)
        {
            Assert.True(dispatcher.TryAcquire(1));
            Assert.True(dispatcher.TryAcquire(2));
        }

        for (var i = 0; i < 6; i++)
        {
            Assert.True(dispatcher.TryAcquire(1));
        }

        Assert.False(dispatcher.TryAcquire(2));
        Assert.False(dispatcher.TryAcquire(1));
    }

    [Fact(Timeout = 5000)]
    public void Unlimited_should_always_acquire_when_totalLimit_is_zero()
    {
        var dispatcher = new FairShareDispatcher(totalLimit: 0, minGuarantee: 10);
        dispatcher.RegisterConnection(1);

        for (var i = 0; i < 1000; i++)
        {
            Assert.True(dispatcher.TryAcquire(1));
        }
    }

    [Fact(Timeout = 5000)]
    public void SlotAvailable_should_notify_when_slot_freed()
    {
        var dispatcher = new FairShareDispatcher(totalLimit: 1, minGuarantee: 1);
        dispatcher.RegisterConnection(1);
        dispatcher.TryAcquire(1);

        var notified = false;
        dispatcher.RegisterSlotAvailableCallback(1, () => notified = true);

        dispatcher.Release(1);
        Assert.True(notified);
    }
}
