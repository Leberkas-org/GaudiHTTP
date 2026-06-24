using GaudiHTTP.Server;

namespace GaudiHTTP.Tests.Server.Options;

public sealed class GaudiServerOptionsValidationSpec
{
    [Fact(Timeout = 5000)]
    public void Validate_should_accept_default_options()
    {
        var options = new GaudiServerOptions();
        options.Validate();
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_reject_negative_MaxRequestBodySize()
    {
        var options = new GaudiServerOptions { Limits = { MaxRequestBodySize = -1 } };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_reject_zero_MaxRequestHeadersTotalSize()
    {
        var options = new GaudiServerOptions { Limits = { MaxRequestHeadersTotalSize = 0 } };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_reject_zero_MaxRequestHeaderCount()
    {
        var options = new GaudiServerOptions { Limits = { MaxRequestHeaderCount = 0 } };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_reject_negative_KeepAliveTimeout()
    {
        var options = new GaudiServerOptions { Limits = { KeepAliveTimeout = TimeSpan.FromSeconds(-1) } };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_reject_zero_HandlerTimeout()
    {
        var options = new GaudiServerOptions { HandlerTimeout = TimeSpan.Zero };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_reject_H2_MaxFrameSize_below_RFC_minimum()
    {
        var options = new GaudiServerOptions { Http2 = { MaxFrameSize = 1024 } };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_reject_H2_MaxFrameSize_above_RFC_maximum()
    {
        var options = new GaudiServerOptions { Http2 = { MaxFrameSize = 16 * 1024 * 1024 + 1 } };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_reject_H2_InitialWindowSize_below_one()
    {
        var options = new GaudiServerOptions { Http2 = { InitialStreamWindowSize = 0 } };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_reject_H2_MaxConcurrentStreams_below_one()
    {
        var options = new GaudiServerOptions { Http2 = { MaxConcurrentStreams = 0 } };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact(Timeout = 5000)]
    public void Validate_should_reject_H3_MaxConcurrentStreams_below_one()
    {
        var options = new GaudiServerOptions { Http3 = { MaxConcurrentStreams = 0 } };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }
}
