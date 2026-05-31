namespace TurboHTTP.Protocol.Server;

internal sealed class DataRateMonitor
{
    private readonly double _minDataRate;
    private readonly long _gracePeriodMs;
    private readonly Dictionary<long, DataRateState> _states = new();

    public DataRateMonitor(double minDataRate, TimeSpan gracePeriod)
    {
        _minDataRate = minDataRate;
        _gracePeriodMs = (long)gracePeriod.TotalMilliseconds;
    }

    public bool Enabled => _minDataRate > 0;
    public int Count => _states.Count;

    public void Observe(long streamId, long bytes, long now)
    {
        if (!Enabled || bytes <= 0)
        {
            return;
        }

        if (!_states.TryGetValue(streamId, out var state))
        {
            state = new DataRateState { LastCheckTimestamp = now, GracePeriodStartTimestamp = now };
            _states[streamId] = state;
        }

        state.TotalBytes += bytes;
    }

    public void Remove(long streamId) => _states.Remove(streamId);

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

            if (rate < _minDataRate)
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
