using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class TurboServerLimitsSpec
{
    [Fact(Timeout = 5000)]
    public void TurboServerLimits_should_default_max_request_body_size_to_30MB()
    {
        var limits = new TurboServerLimits();
        Assert.Equal(30 * 1024 * 1024, limits.MaxRequestBodySize);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerLimits_should_default_max_request_header_count_to_100()
    {
        var limits = new TurboServerLimits();
        Assert.Equal(100, limits.MaxRequestHeaderCount);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerLimits_should_default_max_request_headers_total_size_to_32KB()
    {
        var limits = new TurboServerLimits();
        Assert.Equal(32 * 1024, limits.MaxRequestHeadersTotalSize);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerLimits_should_default_keep_alive_timeout_to_130_seconds()
    {
        var limits = new TurboServerLimits();
        Assert.Equal(TimeSpan.FromSeconds(130), limits.KeepAliveTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerLimits_should_default_request_headers_timeout_to_30_seconds()
    {
        var limits = new TurboServerLimits();
        Assert.Equal(TimeSpan.FromSeconds(30), limits.RequestHeadersTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerLimits_should_default_min_request_body_data_rate_grace_period_to_5_seconds()
    {
        var limits = new TurboServerLimits();
        Assert.Equal(TimeSpan.FromSeconds(5), limits.MinRequestBodyDataRateGracePeriod);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerLimits_should_default_min_response_data_rate_grace_period_to_5_seconds()
    {
        var limits = new TurboServerLimits();
        Assert.Equal(TimeSpan.FromSeconds(5), limits.MinResponseDataRateGracePeriod);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerLimits_should_allow_max_concurrent_connections_to_be_set()
    {
        var limits = new TurboServerLimits { MaxConcurrentConnections = 1000 };
        Assert.Equal(1000, limits.MaxConcurrentConnections);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerLimits_should_allow_max_concurrent_upgraded_connections_to_be_set()
    {
        var limits = new TurboServerLimits { MaxConcurrentUpgradedConnections = 500 };
        Assert.Equal(500, limits.MaxConcurrentUpgradedConnections);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerOptions_Limits_should_delegate_MaxConcurrentConnections_to_deprecated_property()
    {
        var options = new TurboServerOptions();
        options.MaxConcurrentConnections = 1000;
        Assert.Equal(1000, options.Limits.MaxConcurrentConnections);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerOptions_deprecated_MaxConcurrentConnections_should_delegate_to_Limits()
    {
        var options = new TurboServerOptions();
        options.Limits.MaxConcurrentConnections = 2000;
        Assert.Equal(2000, options.MaxConcurrentConnections);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerOptions_Limits_should_delegate_MaxConcurrentUpgradedConnections_to_deprecated_property()
    {
        var options = new TurboServerOptions();
        options.MaxConcurrentUpgradedConnections = 500;
        Assert.Equal(500, options.Limits.MaxConcurrentUpgradedConnections);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerOptions_deprecated_MaxConcurrentUpgradedConnections_should_delegate_to_Limits()
    {
        var options = new TurboServerOptions();
        options.Limits.MaxConcurrentUpgradedConnections = 300;
        Assert.Equal(300, options.MaxConcurrentUpgradedConnections);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerOptions_Limits_should_delegate_KeepAliveTimeout_to_deprecated_property()
    {
        var options = new TurboServerOptions();
        var timeout = TimeSpan.FromSeconds(60);
        options.KeepAliveTimeout = timeout;
        Assert.Equal(timeout, options.Limits.KeepAliveTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerOptions_deprecated_KeepAliveTimeout_should_delegate_to_Limits()
    {
        var options = new TurboServerOptions();
        var timeout = TimeSpan.FromSeconds(90);
        options.Limits.KeepAliveTimeout = timeout;
        Assert.Equal(timeout, options.KeepAliveTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerOptions_Limits_should_delegate_RequestHeadersTimeout_to_deprecated_property()
    {
        var options = new TurboServerOptions();
        var timeout = TimeSpan.FromSeconds(15);
        options.RequestHeadersTimeout = timeout;
        Assert.Equal(timeout, options.Limits.RequestHeadersTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TurboServerOptions_deprecated_RequestHeadersTimeout_should_delegate_to_Limits()
    {
        var options = new TurboServerOptions();
        var timeout = TimeSpan.FromSeconds(45);
        options.Limits.RequestHeadersTimeout = timeout;
        Assert.Equal(timeout, options.RequestHeadersTimeout);
    }
}
