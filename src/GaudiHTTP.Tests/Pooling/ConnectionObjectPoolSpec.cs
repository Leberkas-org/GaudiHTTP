using GaudiHTTP.Pooling;

namespace GaudiHTTP.Tests.Pooling;

public sealed class ConnectionObjectPoolSpec
{
    private sealed class Counter : IResetable
    {
        public int Value;
        public int ResetCount;
        public void Reset()
        {
            Value = 0;
            ResetCount++;
        }
        public void Dispose() => ConnectionObjectPool.Instance.Return(this);
    }

    private sealed class PoolableCounter : Poolable<PoolableCounter>
    {
        public int Value;
        public int ResetCount;
        protected override void OnReset()
        {
            Value = 0;
            ResetCount++;
        }
    }

    [Fact(Timeout = 5000)]
    public void Rent_after_return_reuses_the_same_reset_instance()
    {
        var ctx = ConnectionObjectPool.Instance;
        var a = ctx.Rent(static () => new Counter());
        a.Value = 42;
        ctx.Return(a);

        var b = ctx.Rent(static () => new Counter());

        Assert.Same(a, b);
        Assert.Equal(0, b.Value);

        // Drain so this test doesn't leak a pooled Counter into other tests sharing the singleton.
        ctx.Return(b);
    }

    [Fact(Timeout = 5000)]
    public void Dispose_on_a_Poolable_returns_it_to_the_singleton_pool_reset()
    {
        var ctx = ConnectionObjectPool.Instance;
        var a = ctx.Rent(static () => new PoolableCounter());
        a.Value = 7;

        a.Dispose();

        var b = ctx.Rent(static () => new PoolableCounter());

        Assert.Same(a, b);
        Assert.Equal(0, b.Value);

        ctx.Return(b);
    }

    [Fact(Timeout = 5000)]
    public void Double_dispose_on_a_Poolable_is_a_safe_no_op()
    {
        var ctx = ConnectionObjectPool.Instance;
        var a = ctx.Rent(static () => new PoolableCounter());

        a.Dispose();
        var resetCountAfterFirstDispose = a.ResetCount;

        // A second Dispose must not return the instance to the pool a second time (which would
        // otherwise let two live rentals alias the same object).
        a.Dispose();

        Assert.Equal(resetCountAfterFirstDispose, a.ResetCount);

        var b = ctx.Rent(static () => new PoolableCounter());
        ctx.Return(b);
    }
}
