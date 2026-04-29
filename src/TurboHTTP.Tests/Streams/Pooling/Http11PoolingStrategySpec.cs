using Servus.Akka.Transport;

namespace Servus.Akka.Tests.Transport.Tcp;

public sealed class Http11PoolingStrategySpec
{
    private static readonly TransportOptions TestOptions = new TcpTransportOptions
    {
        Host = "example.com",
        Port = 80
    };

    [Fact(Timeout = 5000)]
    public void MaxConnectionsPerHost_should_default_to_6()
    {
        var strategy = new Http11PoolingStrategy();
        Assert.Equal(6, strategy.MaxConnectionsPerHost);
    }

    [Fact(Timeout = 5000)]
    public void MaxConnectionsPerHost_should_accept_custom_value()
    {
        var strategy = new Http11PoolingStrategy(maxConnectionsPerHost: 10);
        Assert.Equal(10, strategy.MaxConnectionsPerHost);
    }

    [Fact(Timeout = 5000)]
    public void CanReuse_should_return_true()
    {
        var strategy = new Http11PoolingStrategy();
        Assert.True(strategy.CanReuse(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void OnRelease_should_return_Reuse()
    {
        var strategy = new Http11PoolingStrategy();
        Assert.Equal(PoolAction.Reuse, strategy.OnRelease(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void IdleTimeout_should_have_default()
    {
        var strategy = new Http11PoolingStrategy();
        Assert.True(strategy.IdleTimeout > TimeSpan.Zero);
    }

    [Fact(Timeout = 5000)]
    public void IdleTimeout_should_accept_custom_value()
    {
        var strategy = new Http11PoolingStrategy(idleTimeout: TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(5), strategy.IdleTimeout);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLifetime_should_accept_custom_value()
    {
        var strategy = new Http11PoolingStrategy(connectionLifetime: TimeSpan.FromMinutes(20));
        Assert.Equal(TimeSpan.FromMinutes(20), strategy.ConnectionLifetime);
    }

    [Fact(Timeout = 5000)]
    public void OnUpstreamFinish_should_return_Reuse()
    {
        var strategy = new Http11PoolingStrategy();
        Assert.Equal(PoolAction.Reuse, strategy.OnUpstreamFinish(new object()));
    }

    [Fact(Timeout = 5000)]
    public void OnIdle_should_return_Dispose()
    {
        var strategy = new Http11PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnIdle(new object()));
    }

    [Fact(Timeout = 5000)]
    public void OnDisconnect_should_return_Dispose()
    {
        var strategy = new Http11PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnDisconnect(new object(), DisconnectReason.Error));
    }
}
