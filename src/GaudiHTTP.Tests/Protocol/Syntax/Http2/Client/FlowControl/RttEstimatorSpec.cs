using Microsoft.Extensions.Time.Testing;
using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2.Client.FlowControl;

public sealed class RttEstimatorSpec
{
    [Fact(Timeout = 5000)]
    public void RttEstimator_should_report_unknown_rtt_before_any_sample()
    {
        var clock = new FakeTimeProvider();
        var rtt = new RttEstimator(clock, TimeSpan.FromMilliseconds(100));

        Assert.Equal(TimeSpan.Zero, rtt.MinRtt);
    }

    [Fact(Timeout = 5000)]
    public void RttEstimator_should_measure_rtt_from_ping_to_ack()
    {
        var clock = new FakeTimeProvider();
        var rtt = new RttEstimator(clock, TimeSpan.FromMilliseconds(100));

        rtt.OnPingSent();
        clock.Advance(TimeSpan.FromMilliseconds(40));
        rtt.OnPingAck();

        Assert.Equal(TimeSpan.FromMilliseconds(40), rtt.MinRtt);
    }

    [Fact(Timeout = 5000)]
    public void RttEstimator_should_keep_minimum_across_samples()
    {
        var clock = new FakeTimeProvider();
        var rtt = new RttEstimator(clock, TimeSpan.FromMilliseconds(1));

        rtt.OnPingSent();
        clock.Advance(TimeSpan.FromMilliseconds(40));
        rtt.OnPingAck();

        rtt.OnPingSent();
        clock.Advance(TimeSpan.FromMilliseconds(20));
        rtt.OnPingAck();

        rtt.OnPingSent();
        clock.Advance(TimeSpan.FromMilliseconds(80));
        rtt.OnPingAck();

        Assert.Equal(TimeSpan.FromMilliseconds(20), rtt.MinRtt);
    }

    [Fact(Timeout = 5000)]
    public void RttEstimator_should_not_send_ping_before_interval_elapses()
    {
        var clock = new FakeTimeProvider();
        var rtt = new RttEstimator(clock, TimeSpan.FromMilliseconds(100));

        Assert.True(rtt.ShouldSendPing());
        rtt.OnPingSent();
        rtt.OnPingAck();

        clock.Advance(TimeSpan.FromMilliseconds(50));
        Assert.False(rtt.ShouldSendPing());

        clock.Advance(TimeSpan.FromMilliseconds(60));
        Assert.True(rtt.ShouldSendPing());
    }

    [Fact(Timeout = 5000)]
    public void RttEstimator_should_not_send_ping_while_awaiting_ack()
    {
        var clock = new FakeTimeProvider();
        var rtt = new RttEstimator(clock, TimeSpan.FromMilliseconds(1));

        rtt.OnPingSent();
        clock.Advance(TimeSpan.FromMilliseconds(10));

        Assert.False(rtt.ShouldSendPing());
    }
}
