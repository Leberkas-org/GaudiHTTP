using System.Collections.Concurrent;
using Microsoft.Extensions.ObjectPool;

namespace GaudiHTTP.Pooling;

// Process-wide singleton. Owns per-type ObjectPool instances keyed by type. DefaultObjectPool's
// internal thread-safety absorbs the Akka dispatcher thread-hops within and across connections
// (the failure mode that made [ThreadStatic] miss).
internal sealed class ConnectionObjectPool
{
    public static readonly ConnectionObjectPool Instance = new();

    private readonly ConcurrentDictionary<Type, object> _pools = new();

    public T Rent<T>(Func<T> factory) where T : class, IResetable
    {
        var holder = GetHolder<T>();
        holder.Policy.EnsureFactory(factory);
        var obj = holder.Pool.Get();
        obj.OnRented();
        return obj;
    }

    // Must never require a factory: pooled objects self-return on Dispose, and the first pool
    // touch for a type may be a Return (an object constructed directly rather than rented).
    // The factory is latched later by the first Rent.
    public void Return<T>(T obj) where T : class, IResetable
        => GetHolder<T>().Pool.Return(obj);

    private PoolHolder<T> GetHolder<T>() where T : class, IResetable
        => (PoolHolder<T>)_pools.GetOrAdd(typeof(T), static _ => new PoolHolder<T>());

    private sealed class PoolHolder<T> where T : class, IResetable
    {
        public readonly ResettablePoolPolicy<T> Policy = new(null);
        public readonly ObjectPool<T> Pool;

        public PoolHolder()
        {
            Pool = new DefaultObjectPool<T>(Policy, maximumRetained: 256);
        }
    }
}
