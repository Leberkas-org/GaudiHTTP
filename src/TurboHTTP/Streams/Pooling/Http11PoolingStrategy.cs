namespace Servus.Akka.Transport;

public sealed class Http11PoolingStrategy : IPoolingStrategy
{
    public int MaxConnectionsPerHost { get; }
    public TimeSpan IdleTimeout { get; }
    public TimeSpan ConnectionLifetime { get; }

    public Http11PoolingStrategy(
        int maxConnectionsPerHost = 6,
        TimeSpan? idleTimeout = null,
        TimeSpan? connectionLifetime = null)
    {
        MaxConnectionsPerHost = maxConnectionsPerHost;
        IdleTimeout = idleTimeout ?? TimeSpan.FromMinutes(2);
        ConnectionLifetime = connectionLifetime ?? TimeSpan.FromMinutes(10);
    }

    public bool CanReuse(TransportOptions options) => true;
    public PoolAction OnRelease(TransportOptions options) => PoolAction.Reuse;
    public PoolAction OnIdle(object lease) => PoolAction.Dispose;
    public PoolAction OnDisconnect(object lease, DisconnectReason reason) => PoolAction.Dispose;
    public PoolAction OnUpstreamFinish(object lease) => PoolAction.Reuse;
}
