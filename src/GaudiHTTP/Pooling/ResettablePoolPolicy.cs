using Microsoft.Extensions.ObjectPool;

namespace GaudiHTTP.Pooling;

// Creates instances via an injected factory (handles ctor args) and resets them on return. One
// policy type serves every pooled object kind. The factory is latched on the first Rent rather
// than required at construction, because the first pool touch for a type may be a Return (a
// directly constructed object disposing itself back into the pool) — see ConnectionObjectPool.
internal sealed class ResettablePoolPolicy<T>(Func<T>? factory) : IPooledObjectPolicy<T>
    where T : class, IResetable
{
    private Func<T>? _factory = factory;

    public void EnsureFactory(Func<T> create) => _factory ??= create;

    public T Create() => (_factory ?? throw new InvalidOperationException(
        $"No factory registered for pooled type {typeof(T).Name}."))();

    public bool Return(T obj)
    {
        obj.Reset();
        return true;
    }
}
