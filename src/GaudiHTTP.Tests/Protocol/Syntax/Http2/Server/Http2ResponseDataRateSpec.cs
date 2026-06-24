using GaudiHTTP.Protocol;
using GaudiHTTP.Server;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Server;

public sealed class Http2ResponseDataRateSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Response_data_rate_monitor_should_be_initialized()
    {
        var options = new TurboServerOptions
        {
            Http2 = { MinResponseDataRate = 1_000_000, MinResponseDataRateGracePeriod = TimeSpan.FromSeconds(5) }
        };

        var rateOptions = options.ToHttp2Options().ToRateMonitor();

        Assert.Equal(1_000_000, rateOptions.MinResponseDataRate);
        Assert.Equal(TimeSpan.FromSeconds(5), rateOptions.MinResponseDataRateGracePeriod);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Response_data_rate_monitor_should_track_violations()
    {
        var options = new TurboServerOptions
        {
            Http2 = { MinResponseDataRate = 1_000_000, MinResponseDataRateGracePeriod = TimeSpan.FromMilliseconds(100) }
        };

        var rateOptions = options.ToHttp2Options().ToRateMonitor();
        var responseMonitor = new DataRateMonitor(rateOptions.MinResponseDataRate, rateOptions.MinResponseDataRateGracePeriod);

        var now = Environment.TickCount64;

        // Observe a small amount of data (100 bytes)
        responseMonitor.Observe(streamId: 1, bytes: 100, now: now);

        Assert.Equal(1, responseMonitor.Count);

        // At initial check, should be in grace period (not a violation yet)
        var violations = new List<long>();
        responseMonitor.Check(now + 550, violations);

        // No violation yet (grace period not expired)
        Assert.Empty(violations);

        // Wait past grace period (100ms) and check again
        violations.Clear();
        responseMonitor.Check(now + 1100, violations);

        // Should have violation now (grace period expired, rate still below minimum)
        Assert.Single(violations);
        Assert.Equal(1L, violations[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Response_data_rate_can_be_disabled()
    {
        var options = new TurboServerOptions
        {
            Http2 = { MinResponseDataRate = 0 }
        };

        var rateOptions = options.ToHttp2Options().ToRateMonitor();
        var responseMonitor = new DataRateMonitor(rateOptions.MinResponseDataRate, rateOptions.MinResponseDataRateGracePeriod);

        var now = Environment.TickCount64;

        // Observe data
        responseMonitor.Observe(streamId: 1, bytes: 1, now: now);

        // When disabled (rate = 0), Observe should not track anything
        Assert.Equal(0, responseMonitor.Count);

        // Check should do nothing when disabled
        var violations = new List<long>();
        responseMonitor.Check(now + 10000, violations);

        Assert.Empty(violations);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Response_data_rate_recovery_should_exit_grace_period()
    {
        var options = new TurboServerOptions
        {
            Http2 = { MinResponseDataRate = 1_000_000 }
        };

        var rateOptions = options.ToHttp2Options().ToRateMonitor();
        var responseMonitor = new DataRateMonitor(rateOptions.MinResponseDataRate, rateOptions.MinResponseDataRateGracePeriod);

        var now = Environment.TickCount64;

        // Observe a small amount (violates rate)
        responseMonitor.Observe(streamId: 1, bytes: 100, now: now);

        var violations = new List<long>();

        // First check: enters grace period
        responseMonitor.Check(now + 550, violations);
        Assert.Empty(violations);

        // Second check: still in grace period (not expired yet)
        violations.Clear();
        responseMonitor.Check(now + 650, violations);
        Assert.Empty(violations);

        // Now observe a large burst of data (high rate)
        responseMonitor.Observe(streamId: 1, bytes: 10_000_000, now: now + 700);

        // Check again: rate should be high now, exiting grace period
        violations.Clear();
        responseMonitor.Check(now + 1200, violations);
        Assert.Empty(violations);
    }
}
