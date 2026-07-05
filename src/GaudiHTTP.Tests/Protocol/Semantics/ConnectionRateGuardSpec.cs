using GaudiHTTP.Protocol.Semantics;
using GaudiHTTP.Server;
using GaudiHTTP.Tests.Shared;

namespace GaudiHTTP.Tests.Protocol.Semantics;

public sealed class ConnectionRateGuardSpec
{
    private static readonly DataRateOptions Rate = new(
        MinRequestBodyDataRate: 100,
        MinRequestBodyDataRateGracePeriod: TimeSpan.FromSeconds(2),
        MinResponseDataRate: 100,
        MinResponseDataRateGracePeriod: TimeSpan.FromSeconds(2));

    private sealed class FakeClock(long startMs) : TimeProvider
    {
        private long _nowMs = startMs;

        public void Advance(TimeSpan by) => _nowMs += (long)by.TotalMilliseconds;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(_nowMs);
    }

    [Fact(Timeout = 5000)]
    public void ObserveRequest_should_schedule_the_rate_check_timer()
    {
        var ops = new FakeServerOps();
        var guard = new ConnectionRateGuard(ops, Rate);

        guard.ObserveRequest(0, 10);

        Assert.Contains(ops.ScheduledTimers, t => t.Name == ConnectionRateGuard.TimerName);
    }

    [Fact(Timeout = 5000)]
    public void ObserveRequest_should_not_reschedule_while_timer_is_active()
    {
        var ops = new FakeServerOps();
        var guard = new ConnectionRateGuard(ops, Rate);

        guard.ObserveRequest(0, 10);
        guard.ObserveResponse(0, 10);

        Assert.Single(ops.ScheduleTimerCalls);
    }

    [Fact(Timeout = 5000)]
    public void OnTimerFired_should_rearm_when_below_grace_and_still_tracking()
    {
        var clock = new FakeClock(0);
        var ops = new FakeServerOps();
        var guard = new ConnectionRateGuard(ops, Rate, clock);

        guard.ObserveRequest(0, 10);
        clock.Advance(TimeSpan.FromSeconds(1));

        var violated = guard.OnTimerFired();

        Assert.False(violated);
        Assert.Contains(ops.ScheduledTimers, t => t.Name == ConnectionRateGuard.TimerName);
    }

    [Fact(Timeout = 5000)]
    public void OnTimerFired_should_trip_and_stop_rearming_after_grace_period_elapses()
    {
        var clock = new FakeClock(0);
        var ops = new FakeServerOps();
        var guard = new ConnectionRateGuard(ops, Rate, clock);

        guard.ObserveRequest(0, 10);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(guard.OnTimerFired());

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(guard.OnTimerFired());

        clock.Advance(TimeSpan.FromSeconds(2));
        var violated = guard.OnTimerFired();

        Assert.True(violated);
    }

    [Fact(Timeout = 5000)]
    public void OnTimerFired_should_invoke_the_violation_callback_with_entry_counts()
    {
        var clock = new FakeClock(0);
        var ops = new FakeServerOps();
        var guard = new ConnectionRateGuard(ops, Rate, clock);

        guard.ObserveRequest(0, 10);
        guard.ObserveResponse(0, 10);

        clock.Advance(TimeSpan.FromSeconds(1));
        guard.OnTimerFired();
        clock.Advance(TimeSpan.FromSeconds(1));
        guard.OnTimerFired();
        clock.Advance(TimeSpan.FromSeconds(2));

        int? reqCount = null;
        int? respCount = null;
        guard.OnTimerFired((req, resp) =>
        {
            reqCount = req;
            respCount = resp;
        });

        Assert.Equal(1, reqCount);
        Assert.Equal(1, respCount);
    }

    [Fact(Timeout = 5000)]
    public void Remove_should_stop_tracking_the_stream()
    {
        var clock = new FakeClock(0);
        var ops = new FakeServerOps();
        var guard = new ConnectionRateGuard(ops, Rate, clock);

        guard.ObserveRequest(0, 10);
        guard.RemoveRequest(0);

        clock.Advance(TimeSpan.FromSeconds(10));
        var violated = guard.OnTimerFired();

        Assert.False(violated);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_cancel_the_timer_and_allow_it_to_be_rescheduled()
    {
        var ops = new FakeServerOps();
        var guard = new ConnectionRateGuard(ops, Rate);

        guard.ObserveRequest(0, 10);
        guard.Cleanup();

        Assert.Contains(ops.CancelledTimers, t => t == ConnectionRateGuard.TimerName);

        guard.ObserveRequest(0, 10);
        Assert.Equal(2, ops.ScheduleTimerCalls.Count);
    }
}
