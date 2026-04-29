using Servus.Akka.Transport;

namespace Servus.Akka.Tests.Transport;

public sealed class PoolingStrategySpec
{
    private static readonly TransportOptions TestOptions = new TcpTransportOptions
    {
        Host = "example.com",
        Port = 80
    };

    [Fact(Timeout = 5000)]
    public void NoReuse_should_always_return_false_for_CanReuse()
    {
        var strategy = new NoReuseStrategy();

        Assert.False(strategy.CanReuse(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void NoReuse_should_always_return_Dispose_on_release()
    {
        var strategy = new NoReuseStrategy();

        Assert.Equal(PoolAction.Dispose, strategy.OnRelease(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void Reuse_should_return_true_for_CanReuse()
    {
        var strategy = new ReuseStrategy(maxConnectionsPerHost: 6);

        Assert.True(strategy.CanReuse(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void Reuse_should_return_Reuse_on_release()
    {
        var strategy = new ReuseStrategy(maxConnectionsPerHost: 6);

        Assert.Equal(PoolAction.Reuse, strategy.OnRelease(TestOptions));
    }

    [Fact(Timeout = 5000)]
    public void Reuse_should_expose_max_connections_per_host()
    {
        var strategy = new ReuseStrategy(maxConnectionsPerHost: 10);

        Assert.Equal(10, strategy.MaxConnectionsPerHost);
    }

    [Fact(Timeout = 5000)]
    public void IPoolingStrategy_should_expose_idle_timeout()
    {
        var strategy = new ReuseStrategy(maxConnectionsPerHost: 6);

        Assert.Equal(TimeSpan.FromMinutes(2), strategy.IdleTimeout);
    }

    [Fact(Timeout = 5000)]
    public void IPoolingStrategy_should_expose_connection_lifetime()
    {
        var strategy = new ReuseStrategy(maxConnectionsPerHost: 6);

        Assert.Equal(TimeSpan.FromMinutes(10), strategy.ConnectionLifetime);
    }

    private sealed class NoReuseStrategy : IPoolingStrategy
    {
        public int MaxConnectionsPerHost => 1;
        public TimeSpan IdleTimeout => TimeSpan.Zero;
        public TimeSpan ConnectionLifetime => TimeSpan.Zero;

        public bool CanReuse(TransportOptions options) => false;
        public PoolAction OnRelease(TransportOptions options) => PoolAction.Dispose;
    }

    private sealed class ReuseStrategy(int maxConnectionsPerHost) : IPoolingStrategy
    {
        public int MaxConnectionsPerHost => maxConnectionsPerHost;
        public TimeSpan IdleTimeout => TimeSpan.FromMinutes(2);
        public TimeSpan ConnectionLifetime => TimeSpan.FromMinutes(10);

        public bool CanReuse(TransportOptions options) => true;
        public PoolAction OnRelease(TransportOptions options) => PoolAction.Reuse;
    }
}
