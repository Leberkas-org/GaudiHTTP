using GaudiHTTP.Protocol;
using GaudiHTTP.Server;
using GaudiHTTP.Streams.Stages.Server;

namespace GaudiHTTP.Protocol.Semantics;

/// <summary>
/// Owns the request/response data-rate orchestration cluster shared by the HTTP/1.0 and HTTP/1.1
/// server state machines: a pair of <see cref="DataRateMonitor"/>s (one per direction), the
/// violation list reused across timer firings, and the single-shot "data-rate-check" timer.
/// Callers observe bytes as they flow, and route the connection's timer-fired dispatch through
/// <see cref="OnTimerFired"/> when the timer name matches <see cref="TimerName"/>.
/// </summary>
internal sealed class ConnectionRateGuard
{
    public const string TimerName = "data-rate-check";

    private readonly IServerStageOperations _ops;
    private readonly TimeProvider _clock;
    private readonly DataRateMonitor _requestRate;
    private readonly DataRateMonitor _responseRate;
    private readonly List<long> _violations = [];
    private bool _timerActive;

    public ConnectionRateGuard(IServerStageOperations ops, DataRateOptions rate, TimeProvider? clock = null)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        _clock = clock ?? TimeProvider.System;
        _requestRate = new DataRateMonitor(rate.MinRequestBodyDataRate, rate.MinRequestBodyDataRateGracePeriod);
        _responseRate = new DataRateMonitor(rate.MinResponseDataRate, rate.MinResponseDataRateGracePeriod);
    }

    private long Now() => _clock.GetUtcNow().ToUnixTimeMilliseconds();

    public void ObserveRequest(long streamId, long bytes)
    {
        _requestRate.Observe(streamId, bytes, Now());
        EnsureTimer();
    }

    public void ObserveResponse(long streamId, long bytes)
    {
        _responseRate.Observe(streamId, bytes, Now());
        EnsureTimer();
    }

    public void RemoveRequest(long streamId)
    {
        _requestRate.Remove(streamId);
    }

    public void RemoveResponse(long streamId)
    {
        _responseRate.Remove(streamId);
    }

    /// <summary>
    /// Handles the <see cref="TimerName"/> timer firing: checks both monitors for violations and
    /// rearms the timer if either still has active entries. Returns <see langword="true"/> if a
    /// violation was detected, in which case <paramref name="onViolation"/> (if given) is invoked
    /// with the current request/response entry counts so the caller can log before tearing the
    /// connection down — the guard itself never rearms after a violation.
    /// </summary>
    public bool OnTimerFired(Action<int, int>? onViolation = null)
    {
        _timerActive = false;
        _violations.Clear();
        var now = Now();
        _requestRate.Check(now, _violations);
        _responseRate.Check(now, _violations);

        if (_violations.Count > 0)
        {
            onViolation?.Invoke(_requestRate.Count, _responseRate.Count);
            return true;
        }

        if (_requestRate.Count > 0 || _responseRate.Count > 0)
        {
            EnsureTimer();
        }

        return false;
    }

    public void EnsureTimer()
    {
        if (_timerActive)
        {
            return;
        }

        _timerActive = true;
        _ops.OnScheduleTimer(TimerName, TimeSpan.FromSeconds(1));
    }

    public void Cleanup()
    {
        _ops.OnCancelTimer(TimerName);
        _timerActive = false;
    }
}
