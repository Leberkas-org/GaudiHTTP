using Servus.Akka.Transport;

namespace Servus.Akka.Tests.Transport.Tcp;

public sealed class Http2PoolingStrategySpec
{
    private static readonly TransportOptions TestOptions = new TlsTransportOptions
    {
        Host = "example.com",
        Port = 443
    };

    [Fact(Timeout = 5000)]
    public void MaxConnectionsPerHost_should_be_1()
    {
        var strategy = new Http2PoolingStrategy();
        Assert.Equal(1, strategy.MaxConnectionsPerHost);
    }

    [Fact(Timeout = 5000)]
    public void CanReuse_should_return_false()
    {
        var strategy = new Http2PoolingStrategy();
        Assert.False(strategy.CanReuse(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void OnRelease_should_return_Dispose()
    {
        var strategy = new Http2PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnRelease(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void OnUpstreamFinish_should_return_Dispose()
    {
        var strategy = new Http2PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnUpstreamFinish(new object()));
    }

    [Fact(Timeout = 5000)]
    public void IdleTimeout_should_accept_custom_value()
    {
        var strategy = new Http2PoolingStrategy(idleTimeout: TimeSpan.FromMinutes(3));
        Assert.Equal(TimeSpan.FromMinutes(3), strategy.IdleTimeout);
    }

    [Fact(Timeout = 5000)]
    public void OnIdle_should_return_Dispose()
    {
        var strategy = new Http2PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnIdle(new object()));
    }

    [Fact(Timeout = 5000)]
    public void OnDisconnect_should_return_Dispose()
    {
        var strategy = new Http2PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnDisconnect(new object(), DisconnectReason.Error));
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLifetime_should_default_to_infinite()
    {
        var strategy = new Http2PoolingStrategy();
        Assert.Equal(Timeout.InfiniteTimeSpan, strategy.ConnectionLifetime);
    }
}
