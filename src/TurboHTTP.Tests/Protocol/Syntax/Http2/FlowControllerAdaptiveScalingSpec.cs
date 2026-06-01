using Microsoft.Extensions.Time.Testing;
using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2;

public sealed class FlowControllerAdaptiveScalingSpec
{
    private const int Start = 64 * 1024;
    private const int Cap = 16 * 1024 * 1024;
    private const int ConnWindow = 64 * 1024 * 1024;

    private static FlowController NewScaling(FakeTimeProvider clock) =>
        new(ConnWindow, Start, new WindowScaler(Cap, 1.0), clock);

    [Fact(Timeout = 5000)]
    public void FlowController_should_grow_stream_window_when_saturated()
    {
        var clock = new FakeTimeProvider();
        var fc = NewScaling(clock);
        fc.MinRtt = TimeSpan.FromMilliseconds(100);

        fc.OnInboundData(1, Start / 2);
        clock.Advance(TimeSpan.FromMilliseconds(10));
        var result = fc.OnInboundData(1, Start / 2);

        Assert.True(result.Success);
        Assert.NotNull(result.StreamWindowUpdate);
        Assert.True(result.StreamWindowUpdate!.Value.Increment > Start);
        Assert.Equal(Start * 2, fc.CurrentStreamWindow);
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_not_grow_when_min_rtt_unknown()
    {
        var clock = new FakeTimeProvider();
        var fc = NewScaling(clock);

        fc.OnInboundData(1, Start / 2);
        clock.Advance(TimeSpan.FromMilliseconds(10));
        fc.OnInboundData(1, Start / 2);

        Assert.Equal(Start, fc.CurrentStreamWindow);
    }

    [Fact(Timeout = 5000)]
    public void FlowController_should_behave_identically_to_static_when_no_scaler()
    {
        var fc = new FlowController(ConnWindow, Start);

        fc.OnInboundData(1, Start / 2);
        var result = fc.OnInboundData(1, Start / 2);

        Assert.Equal(Start, fc.CurrentStreamWindow);
        Assert.NotNull(result.StreamWindowUpdate);
        Assert.Equal(Start / 2, result.StreamWindowUpdate!.Value.Increment);
    }

    [Fact(Timeout = 5000)]
    public void FlowController_reset_should_clear_scaling_state()
    {
        var clock = new FakeTimeProvider();
        var fc = NewScaling(clock);
        fc.MinRtt = TimeSpan.FromMilliseconds(100);

        fc.OnInboundData(1, Start / 2);
        clock.Advance(TimeSpan.FromMilliseconds(10));
        fc.OnInboundData(1, Start / 2);
        Assert.Equal(Start * 2, fc.CurrentStreamWindow);

        fc.Reset(ConnWindow, Start);

        Assert.Equal(Start, fc.CurrentStreamWindow);
        Assert.Equal(TimeSpan.Zero, fc.MinRtt);
    }
}
