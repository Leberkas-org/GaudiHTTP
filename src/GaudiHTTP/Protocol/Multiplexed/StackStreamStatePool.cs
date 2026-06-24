namespace GaudiHTTP.Protocol.Multiplexed;

internal sealed class StackStreamStatePool<TState>(int maxCapacity, Func<TState> factory) : IStreamStatePool<TState>
    where TState : class
{
    private readonly Stack<TState> _pool = new();
    private readonly HashSet<TState> _pooled = new(ReferenceEqualityComparer.Instance);

    public TState Rent()
    {
        if (_pool.Count > 0)
        {
            var item = _pool.Pop();
            _pooled.Remove(item);
            return item;
        }

        return factory();
    }

    public void Return(TState state)
    {
        if (_pool.Count < maxCapacity && _pooled.Add(state))
        {
            _pool.Push(state);
        }
    }
}
