namespace TurboHTTP.Protocol.Multiplexed;

internal sealed class StackStreamStatePool<TState> : IStreamStatePool<TState> where TState : class
{
    private readonly Stack<TState> _pool = new();
    private readonly int _maxCapacity;
    private readonly Func<TState> _factory;

    public StackStreamStatePool(int maxCapacity, Func<TState> factory)
    {
        _maxCapacity = maxCapacity;
        _factory = factory;
    }

    public TState Rent()
    {
        return _pool.Count > 0 ? _pool.Pop() : _factory();
    }

    public void Return(TState state)
    {
        if (_pool.Count < _maxCapacity)
        {
            _pool.Push(state);
        }
    }
}
