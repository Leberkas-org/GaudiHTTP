namespace Servus.Akka.Transport;

public sealed class Http2PoolingStrategy : IPoolingStrategy
{
    public int MaxConnectionsPerHost { get; }
    public TimeSpan IdleTimeout { get; }
    public TimeSpan ConnectionLifetime { get; }

    public Http2PoolingStrategy(
        int maxConnectionsPerHost = 1,
        TimeSpan? idleTimeout = null,
        TimeSpan? connectionLifetime = null)
    {
        MaxConnectionsPerHost = maxConnectionsPerHost;
        IdleTimeout = idleTimeout ?? TimeSpan.FromMinutes(2);
        ConnectionLifetime = connectionLifetime ?? Timeout.InfiniteTimeSpan;
    }

    public bool CanReuse(TransportOptions options) => false;
    public PoolAction OnRelease(TransportOptions options) => PoolAction.Dispose;
    public PoolAction OnIdle(object lease) => PoolAction.Dispose;
    public PoolAction OnDisconnect(object lease, DisconnectReason reason) => PoolAction.Dispose;
    public PoolAction OnUpstreamFinish(object lease) => PoolAction.Dispose;
}
