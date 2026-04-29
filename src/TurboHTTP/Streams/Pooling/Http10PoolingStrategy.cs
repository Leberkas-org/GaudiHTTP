namespace Servus.Akka.Transport;

public sealed class Http10PoolingStrategy : IPoolingStrategy
{
    public int MaxConnectionsPerHost => int.MaxValue;
    public TimeSpan IdleTimeout => TimeSpan.Zero;
    public TimeSpan ConnectionLifetime => TimeSpan.Zero;

    public bool CanReuse(TransportOptions options) => false;
    public PoolAction OnRelease(TransportOptions options) => PoolAction.Dispose;
    public PoolAction OnIdle(object lease) => PoolAction.Dispose;
    public PoolAction OnDisconnect(object lease, DisconnectReason reason) => PoolAction.Dispose;
    public PoolAction OnUpstreamFinish(object lease) => PoolAction.Dispose;
}
