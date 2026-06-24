namespace TurboHTTP.Protocol;

internal sealed class DataRateMonitor(double minDataRate, TimeSpan gracePeriod)
{
    private readonly long _gracePeriodMs = (long)gracePeriod.TotalMilliseconds;
    private readonly Dictionary<long, DataRateState> _states = new();

    // Removed states are parked here and reused on the next Observe-miss. On HTTP/1.x every request
    // reuses streamId 0 (strict Observe-then-Remove), so after warmup this is a per-request
    // allocation eliminated entirely — DataRateState was the single largest hot-path allocator.
    // Actor-confined (one monitor per connection), so no synchronization is required.
    private readonly Stack<DataRateState> _pool = new();

    public bool Enabled => minDataRate > 0;
    public int Count => _states.Count;

    public void Observe(long streamId, long bytes, long now)
    {
        if (!Enabled || bytes <= 0)
        {
            return;
        }

        if (!_states.TryGetValue(streamId, out var state))
        {
            state = _pool.Count > 0 ? _pool.Pop() : new DataRateState();
            state.Reset();
            state.LastCheckTimestamp = now;
            state.GracePeriodStartTimestamp = now;
            _states[streamId] = state;
        }

        state.TotalBytes += bytes;
    }

    public void Remove(long streamId)
    {
        if (_states.Remove(streamId, out var state))
        {
            _pool.Push(state);
        }
    }

    public void Check(long now, List<long> violations)
    {
        if (!Enabled)
        {
            return;
        }

        foreach (var (streamId, state) in _states)
        {
            var elapsedMs = now - state.LastCheckTimestamp;
            if (elapsedMs < 500)
            {
                continue;
            }

            var rate = (state.TotalBytes - state.LastCheckBytes) / (elapsedMs / 1000.0);
            state.LastCheckBytes = state.TotalBytes;
            state.LastCheckTimestamp = now;

            if (rate < minDataRate)
            {
                if (!state.InGracePeriod)
                {
                    state.InGracePeriod = true;
                    state.GracePeriodStartTimestamp = now;
                }
                else if (now - state.GracePeriodStartTimestamp > _gracePeriodMs)
                {
                    violations.Add(streamId);
                }
            }
            else
            {
                state.InGracePeriod = false;
            }
        }
    }
}
