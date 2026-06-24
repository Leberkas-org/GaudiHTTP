using GaudiHTTP.Protocol.Syntax.Http2;

namespace GaudiHTTP.Tests.Protocol.Syntax.Http2;

public sealed class WindowScalerSpec
{
    private const int Start = 64 * 1024;
    private const int Cap = 16 * 1024 * 1024;

    [Fact(Timeout = 5000)]
    public void WindowScaler_should_double_window_when_saturated_at_low_rtt()
    {
        var scaler = new WindowScaler(Cap, multiplier: 1.0);

        var result = scaler.ComputeNewWindow(Start, 1 * 1024 * 1024,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

        Assert.Equal(Start * 2, result);
    }

    [Fact(Timeout = 5000)]
    public void WindowScaler_should_not_grow_when_throughput_below_window()
    {
        var scaler = new WindowScaler(Cap, multiplier: 1.0);

        var result = scaler.ComputeNewWindow(Start, 1024,
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(1));

        Assert.Equal(Start, result);
    }

    [Fact(Timeout = 5000)]
    public void WindowScaler_should_not_grow_when_min_rtt_unknown()
    {
        var scaler = new WindowScaler(Cap, multiplier: 1.0);

        var result = scaler.ComputeNewWindow(Start, 8 * 1024 * 1024,
            TimeSpan.FromMilliseconds(100), TimeSpan.Zero);

        Assert.Equal(Start, result);
    }

    [Fact(Timeout = 5000)]
    public void WindowScaler_should_cap_growth_at_max_window()
    {
        var scaler = new WindowScaler(Cap, multiplier: 1.0);
        var nearCap = Cap - 1024;

        var result = scaler.ComputeNewWindow(nearCap, 64 * 1024 * 1024,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

        Assert.Equal(Cap, result);
    }

    [Fact(Timeout = 5000)]
    public void WindowScaler_should_not_grow_when_already_at_cap()
    {
        var scaler = new WindowScaler(Cap, multiplier: 1.0);

        var result = scaler.ComputeNewWindow(Cap, 64 * 1024 * 1024,
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

        Assert.Equal(Cap, result);
    }

    [Fact(Timeout = 5000)]
    public void WindowScaler_should_grow_less_eagerly_with_higher_multiplier()
    {
        var eager = new WindowScaler(Cap, multiplier: 1.0);
        var lazy = new WindowScaler(Cap, multiplier: 16.0);

        var delivered = (long)(Start * 2);
        var grewEager = eager.ComputeNewWindow(Start, delivered,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));
        var grewLazy = lazy.ComputeNewWindow(Start, delivered,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));

        Assert.Equal(Start * 2, grewEager);
        Assert.Equal(Start, grewLazy);
    }
}
