using System.Collections.Concurrent;
using Microsoft.Extensions.ObjectPool;

namespace GaudiHTTP.Pooling;

// One per connection. Owns per-type ObjectPool instances keyed by type. Per-connection instancing
// means no cross-connection contention; DefaultObjectPool's internal thread-safety absorbs the Akka
// dispatcher thread-hops within a connection (the failure mode that made [ThreadStatic] miss).
internal sealed class ConnectionPoolContext
{
    private readonly ConcurrentDictionary<Type, object> _pools = new();

    public T Rent<T>(Func<T> factory) where T : class, IResettable
        => GetPool(factory).Get();

    public void Return<T>(T obj) where T : class, IResettable
        => GetPool<T>(null).Return(obj);

    private ObjectPool<T> GetPool<T>(Func<T>? factory) where T : class, IResettable
        => (ObjectPool<T>)_pools.GetOrAdd(typeof(T),
            _ => new DefaultObjectPool<T>(
                new ResettablePoolPolicy<T>(factory ?? throw new InvalidOperationException(
                    $"No factory registered for pooled type {typeof(T).Name}.")),
                maximumRetained: 64));
}
