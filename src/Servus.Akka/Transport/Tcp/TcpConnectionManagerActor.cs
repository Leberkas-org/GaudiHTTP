using Akka.Actor;

namespace Servus.Akka.Transport.Tcp;

public sealed class TcpConnectionManagerActor : ReceiveActor, IWithTimers
{
    public sealed record Acquire(
        TransportOptions Options,
        TaskCompletionSource<ConnectionLease> Tcs,
        CancellationToken Token);

    public sealed record Release(ConnectionLease Lease, bool CanReuse);

    private sealed record Established(ConnectionLease Lease, Acquire Original);
    private sealed record EstablishFailed(Exception Ex, Acquire Original);

    private sealed class Evict
    {
        public static readonly Evict Instance = new();
    }

    private sealed class HostState(TransportOptions options, int maxConnections)
    {
        public readonly TransportOptions Options = options;
        public readonly int MaxConnections = maxConnections;
        public readonly List<ConnectionLease> Leases = [];
        public readonly Queue<ConnectionLease> Idle = new();
        public readonly Queue<Acquire> Pending = new();
        public int Establishing;
    }

    private readonly Dictionary<TransportOptions, HostState> _hosts = new();
    private readonly ITcpConnectionFactory _factory;
    private readonly IPoolingStrategy _poolingStrategy;
    private const string EvictTimerKey = "evict-idle";

    public ITimerScheduler Timers { get; set; } = null!;

    public static Task<ConnectionLease> AcquireAsync(
        IActorRef actor, TransportOptions options, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<ConnectionLease>();

        if (ct.CanBeCanceled)
        {
            ct.UnsafeRegister(
                static (state, token) => ((TaskCompletionSource<ConnectionLease>)state!).TrySetCanceled(token),
                tcs);
        }

        actor.Tell(new Acquire(options, tcs, ct));
        return tcs.Task;
    }

    public TcpConnectionManagerActor(ITcpConnectionFactory factory, IPoolingStrategy poolingStrategy)
    {
        _factory = factory;
        _poolingStrategy = poolingStrategy;

        Receive<Acquire>(OnAcquire);
        Receive<Release>(OnRelease);
        Receive<Established>(OnEstablished);
        Receive<EstablishFailed>(OnFailed);
        Receive<Evict>(_ => OnEvict());
    }

    protected override void PreStart()
    {
        if (_poolingStrategy.IdleTimeout > TimeSpan.Zero)
        {
            Timers.StartPeriodicTimer(EvictTimerKey, Evict.Instance,
                _poolingStrategy.IdleTimeout, _poolingStrategy.IdleTimeout);
        }
    }

    private void OnAcquire(Acquire msg)
    {
        if (msg.Tcs.Task.IsCompleted) return;

        var host = GetOrCreateHost(msg.Options);

        while (host.Idle.TryDequeue(out var idle))
        {
            if (idle.IsAlive() && !idle.IsExpired(_poolingStrategy.ConnectionLifetime))
            {
                if (msg.Tcs.TrySetResult(idle))
                {
                    return;
                }
            }
            else
            {
                host.Leases.Remove(idle);
                idle.Dispose();
            }
        }

        if (host.Leases.Count + host.Establishing < host.MaxConnections)
        {
            Establish(host, msg);
        }
        else
        {
            host.Pending.Enqueue(msg);
        }
    }

    private void OnRelease(Release msg)
    {
        var options = FindHostKey(msg.Lease);

        if (options is null || !_hosts.TryGetValue(options, out var host))
        {
            msg.Lease.Dispose();
            return;
        }

        if (!msg.CanReuse || !msg.Lease.IsAlive())
        {
            host.Leases.Remove(msg.Lease);
            msg.Lease.Dispose();
            ServeNextPending(host);
            return;
        }

        while (host.Pending.TryDequeue(out var pending))
        {
            if (!pending.Tcs.Task.IsCompleted)
            {
                if (pending.Tcs.TrySetResult(msg.Lease))
                {
                    return;
                }
            }
        }

        host.Idle.Enqueue(msg.Lease);
    }

    private void OnEstablished(Established msg)
    {
        var host = GetOrCreateHost(msg.Original.Options);
        host.Establishing--;
        host.Leases.Add(msg.Lease);

        if (!msg.Original.Tcs.TrySetResult(msg.Lease))
        {
            OnRelease(new Release(msg.Lease, CanReuse: true));
        }
    }

    private void OnFailed(EstablishFailed msg)
    {
        if (_hosts.TryGetValue(msg.Original.Options, out var host))
        {
            host.Establishing--;
        }

        if (msg.Ex is OperationCanceledException oce)
        {
            msg.Original.Tcs.TrySetCanceled(oce.CancellationToken);
        }
        else
        {
            msg.Original.Tcs.TrySetException(msg.Ex);
        }

        if (host is not null)
        {
            ServeNextPending(host);
        }
    }

    private void OnEvict()
    {
        foreach (var host in _hosts.Values)
        {
            var toRemove = new List<ConnectionLease>();
            var newIdle = new Queue<ConnectionLease>();

            while (host.Idle.TryDequeue(out var lease))
            {
                if (!lease.IsAlive() || lease.IsExpired(_poolingStrategy.ConnectionLifetime))
                {
                    toRemove.Add(lease);
                }
                else
                {
                    newIdle.Enqueue(lease);
                }
            }

            while (newIdle.TryDequeue(out var kept))
            {
                host.Idle.Enqueue(kept);
            }

            foreach (var lease in toRemove)
            {
                host.Leases.Remove(lease);
                lease.Dispose();
            }
        }
    }

    protected override void PostStop()
    {
        Timers.CancelAll();
        foreach (var host in _hosts.Values)
        {
            while (host.Pending.TryDequeue(out var pending))
            {
                pending.Tcs.TrySetException(new ObjectDisposedException(
                    nameof(TcpConnectionManagerActor)));
            }

            foreach (var lease in host.Leases)
            {
                lease.Dispose();
            }
        }

        _hosts.Clear();
    }

    private TransportOptions? FindHostKey(ConnectionLease lease)
    {
        foreach (var (key, host) in _hosts)
        {
            if (host.Leases.Contains(lease))
            {
                return key;
            }
        }

        return null;
    }

    private HostState GetOrCreateHost(TransportOptions options)
    {
        if (!_hosts.TryGetValue(options, out var state))
        {
            state = new HostState(options, _poolingStrategy.MaxConnectionsPerHost);
            _hosts[options] = state;
        }
        return state;
    }

    private void Establish(HostState host, Acquire msg)
    {
        host.Establishing++;
        _factory
            .EstablishAsync(msg.Options, msg.Token)
            .PipeTo(Self,
                success: lease => new Established(lease, msg),
                failure: ex => new EstablishFailed(ex, msg));
    }

    private void ServeNextPending(HostState host)
    {
        while (host.Pending.TryDequeue(out var next))
        {
            if (!next.Tcs.Task.IsCompleted)
            {
                if (host.Leases.Count + host.Establishing < host.MaxConnections)
                {
                    Establish(host, next);
                    return;
                }

                host.Pending.Enqueue(next);
                return;
            }
        }
    }
}
