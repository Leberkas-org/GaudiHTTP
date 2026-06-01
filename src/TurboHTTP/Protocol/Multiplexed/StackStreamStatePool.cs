namespace TurboHTTP.Protocol.Multiplexed;

internal sealed class StackStreamStatePool<TState>(int maxCapacity, Func<TState> factory) : IStreamStatePool<TState>
    where TState : class
{
    private readonly Stack<TState> _pool = new();

    public TState Rent()
    {
        return _pool.Count > 0 ? _pool.Pop() : factory();
    }

    public void Return(TState state)
    {
        if (_pool.Count < maxCapacity)
        {
            _pool.Push(state);
        }
    }
}
