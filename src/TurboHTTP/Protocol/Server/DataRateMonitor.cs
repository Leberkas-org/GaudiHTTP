namespace TurboHTTP.Protocol.Server;

internal sealed class DataRateMonitor(double minDataRate, TimeSpan gracePeriod)
{
    private readonly long _gracePeriodMs = (long)gracePeriod.TotalMilliseconds;
    private readonly Dictionary<long, DataRateState> _states = new();

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
