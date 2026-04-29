using Servus.Akka.Transport;

namespace Servus.Akka.Tests.Transport.Tcp;

public sealed class Http10PoolingStrategySpec
{
    private static readonly TransportOptions TestOptions = new TcpTransportOptions
    {
        Host = "example.com",
        Port = 80
    };

    [Fact(Timeout = 5000)]
    public void MaxConnectionsPerHost_should_be_unlimited()
    {
        var strategy = new Http10PoolingStrategy();
        Assert.Equal(int.MaxValue, strategy.MaxConnectionsPerHost);
    }

    [Fact(Timeout = 5000)]
    public void CanReuse_should_return_false()
    {
        var strategy = new Http10PoolingStrategy();
        Assert.False(strategy.CanReuse(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void OnRelease_should_return_Dispose()
    {
        var strategy = new Http10PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnRelease(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void IdleTimeout_should_be_zero()
    {
        var strategy = new Http10PoolingStrategy();
        Assert.Equal(TimeSpan.Zero, strategy.IdleTimeout);
    }

    [Fact(Timeout = 5000)]
    public void ConnectionLifetime_should_be_zero()
    {
        var strategy = new Http10PoolingStrategy();
        Assert.Equal(TimeSpan.Zero, strategy.ConnectionLifetime);
    }

    [Fact(Timeout = 5000)]
    public void OnIdle_should_return_Dispose()
    {
        var strategy = new Http10PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnIdle(new object()));
    }

    [Fact(Timeout = 5000)]
    public void OnDisconnect_should_return_Dispose()
    {
        var strategy = new Http10PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnDisconnect(new object(), DisconnectReason.Error));
    }

    [Fact(Timeout = 5000)]
    public void OnUpstreamFinish_should_return_Dispose()
    {
        var strategy = new Http10PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnUpstreamFinish(new object()));
    }
}
