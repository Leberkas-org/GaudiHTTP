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
        => GetPool(factory).Get();

    public void Return<T>(T obj) where T : class, IResetable
        => GetPool<T>(null).Return(obj);

    private ObjectPool<T> GetPool<T>(Func<T>? factory) where T : class, IResetable
        => (ObjectPool<T>)_pools.GetOrAdd(typeof(T),
            _ => new DefaultObjectPool<T>(
                new ResettablePoolPolicy<T>(factory ?? throw new InvalidOperationException(
                    $"No factory registered for pooled type {typeof(T).Name}.")),
                maximumRetained: 256));
}
