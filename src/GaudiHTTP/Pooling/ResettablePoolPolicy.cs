using Microsoft.Extensions.ObjectPool;

namespace GaudiHTTP.Pooling;

// Creates instances via an injected factory (handles ctor args) and resets them on return. One
// policy type serves every pooled object kind.
internal sealed class ResettablePoolPolicy<T>(Func<T> factory) : IPooledObjectPolicy<T>
    where T : class, IResettable
{
    public T Create() => factory();

    public bool Return(T obj)
    {
        obj.Reset();
        return true;
    }
}
