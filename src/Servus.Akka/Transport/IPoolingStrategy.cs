namespace Servus.Akka.Transport;

public interface IPoolingStrategy
{
    int MaxConnectionsPerHost { get; }
    TimeSpan IdleTimeout { get; }
    TimeSpan ConnectionLifetime { get; }
    bool CanReuse(TransportOptions options);
    PoolAction OnRelease(TransportOptions options);
    PoolAction OnIdle(object lease);
    PoolAction OnDisconnect(object lease, DisconnectReason reason);
    PoolAction OnUpstreamFinish(object lease);
}
