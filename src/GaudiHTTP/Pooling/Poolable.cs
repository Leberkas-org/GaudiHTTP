namespace GaudiHTTP.Pooling;

// Base class for pooled objects that self-return on Dispose. The `_returned` flag is guarded by
// Interlocked.Exchange rather than actor-thread confinement because Dispose can be invoked from
// arbitrary threads (finalizers, using-blocks on non-actor threads), unlike the actor-owned state
// machines described in CLAUDE.md's threading model.
internal abstract class Poolable<TSelf> : IResetable
    where TSelf : Poolable<TSelf>
{
    private int _returned;

    protected abstract void OnReset();

    public void Reset()
    {
        Interlocked.Exchange(ref _returned, 0);
        OnReset();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _returned, 1) != 0)
        {
            return;
        }

        ConnectionObjectPool.Instance.Return((TSelf)this);
    }
}
