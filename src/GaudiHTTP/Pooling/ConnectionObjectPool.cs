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
        var obj = GetPool(factory).Get();
        obj.OnRented();
        return obj;
    }

    public void Return<T>(T obj) where T : class, IResetable
    {
        // No Rent<T> has created the pool yet (e.g. a directly-constructed instance is disposed).
        // Treat it like returning to a full pool: reset and discard.
        if (_pools.TryGetValue(typeof(T), out var pool))
        {
            ((ObjectPool<T>)pool).Return(obj);
        }
        else
        {
            obj.Reset();
        }
    }

    private ObjectPool<T> GetPool<T>(Func<T> factory) where T : class, IResetable
        => (ObjectPool<T>)_pools.GetOrAdd(typeof(T),
            _ => new DefaultObjectPool<T>(new ResettablePoolPolicy<T>(factory), maximumRetained: 256));
}
