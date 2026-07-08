using GaudiHTTP.Protocol.Semantics;

namespace GaudiHTTP.Tests.Protocol.Semantics;

public sealed class ReconnectBackoffSpec
{
    [Fact(Timeout = 5000)]
    public void Compute_with_zero_jitter_should_grow_geometrically()
    {
        var initial = TimeSpan.FromMilliseconds(100);
        var max = TimeSpan.FromSeconds(30);

        var r1 = ReconnectBackoff.Compute(1, initial, max, 2.0, 0.0, Random.Shared);
        var r2 = ReconnectBackoff.Compute(2, initial, max, 2.0, 0.0, Random.Shared);
        var r3 = ReconnectBackoff.Compute(3, initial, max, 2.0, 0.0, Random.Shared);

        Assert.Equal(100, r1.TotalMilliseconds, 3);
        Assert.Equal(200, r2.TotalMilliseconds, 3);
        Assert.Equal(400, r3.TotalMilliseconds, 3);
    }

    [Fact(Timeout = 5000)]
    public void Compute_should_cap_at_max()
    {
        var max = TimeSpan.FromSeconds(5);

        var delay = ReconnectBackoff.Compute(20, TimeSpan.FromMilliseconds(100), max, 2.0, 0.0, Random.Shared);

        Assert.Equal(max.TotalMilliseconds, delay.TotalMilliseconds, 3);
    }

    [Fact(Timeout = 5000)]
    public void Compute_should_apply_symmetric_jitter_within_bounds()
    {
        var initial = TimeSpan.FromMilliseconds(1000);
        var max = TimeSpan.FromSeconds(30);

        for (var i = 0; i < 200; i++)
        {
            var delay = ReconnectBackoff.Compute(1, initial, max, 2.0, 0.2, Random.Shared).TotalMilliseconds;
            Assert.InRange(delay, 800, 1200);
        }
    }

    [Fact(Timeout = 5000)]
    public void Compute_should_never_return_below_one_millisecond()
    {
        var delay = ReconnectBackoff.Compute(1, TimeSpan.FromMilliseconds(0.5),
            TimeSpan.FromSeconds(1), 2.0, 0.2, Random.Shared);

        Assert.True(delay >= TimeSpan.FromMilliseconds(1), $"expected >= 1ms, was {delay.TotalMilliseconds}ms");
    }
}
