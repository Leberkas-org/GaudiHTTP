using GaudiHTTP.Protocol;

namespace GaudiHTTP.Tests.Protocol;

public sealed class DataRateMonitorSpec
{
    private const long Sec = 1000;

    [Fact(Timeout = 5000)]
    public void Disabled_when_rate_not_positive()
    {
        var m = new DataRateMonitor(minDataRate: 0, gracePeriod: TimeSpan.FromSeconds(5));
        Assert.False(m.Enabled);
    }

    [Fact(Timeout = 5000)]
    public void Fast_transfer_should_not_violate()
    {
        var m = new DataRateMonitor(minDataRate: 100, gracePeriod: TimeSpan.FromSeconds(5));
        m.Observe(streamId: 1, bytes: 1000, now: 0);
        m.Observe(streamId: 1, bytes: 1000, now: Sec);
        var violations = new List<long>();
        m.Check(now: Sec, violations);
        Assert.Empty(violations);
    }

    [Fact(Timeout = 5000)]
    public void Slow_transfer_should_violate_after_grace()
    {
        var m = new DataRateMonitor(minDataRate: 100, gracePeriod: TimeSpan.FromSeconds(2));
        m.Observe(1, bytes: 10, now: 0);

        var v = new List<long>();
        m.Check(now: 1 * Sec, v); Assert.Empty(v);
        m.Check(now: 2 * Sec, v); Assert.Empty(v);
        m.Check(now: 4 * Sec, v); Assert.Contains(1L, v);
    }

    [Fact(Timeout = 5000)]
    public void Streams_should_be_independent()
    {
        var m = new DataRateMonitor(minDataRate: 100, gracePeriod: TimeSpan.FromSeconds(1));
        m.Observe(1, 10, now: 0);
        m.Observe(2, 10_000, now: 0);
        m.Observe(2, 10_000, now: Sec);

        var v = new List<long>();
        m.Check(now: 1 * Sec, v);
        m.Check(now: 3 * Sec, v);
        Assert.Contains(1L, v);
        Assert.DoesNotContain(2L, v);
    }

    [Fact(Timeout = 5000)]
    public void Remove_should_stop_tracking()
    {
        var m = new DataRateMonitor(minDataRate: 100, gracePeriod: TimeSpan.FromSeconds(1));
        m.Observe(1, 10, now: 0);
        m.Remove(1);
        var v = new List<long>();
        m.Check(now: 5 * Sec, v);
        Assert.Empty(v);
        Assert.Equal(0, m.Count);
    }
}
