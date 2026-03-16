using System;
using System.Collections.Generic;
using System.Linq;
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Dsl;
using Servus.Akka;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class HostPoolActor : ReceiveActor
{
    public record HostPoolConfig(TcpOptions Options, PoolConfig Config, HostKey Key);

    // ── Public message protocol ───────────────────────────────────────

    public sealed record ConnectionIdle(IActorRef Connection);

    public sealed record ConnectionFailed(IActorRef Connection);

    public sealed record IdleCheck;

    public sealed record Reconnect(IActorRef Connection);

    public sealed record MarkConnectionNoReuse(IActorRef Connection);

    /// <summary>
    /// Retained for backward compatibility — ConnectionActor still sends this message.
    /// The handler is a no-op; will be fully removed in TASK-5A-007.
    /// </summary>
    public sealed record RegisterConnectionRefs(
        IActorRef Connection,
        Source<DataItem, NotUsed> ResponseSource);

    // ── Fields ────────────────────────────────────────────────────────

    private readonly HostKey _key;
    private readonly TcpOptions _options;
    private readonly PoolConfig _config;
    private ICancelable? _scheduler;

    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly List<ConnectionState> _connections = [];

    /// <summary>Active connection handle (from the most recent ConnectionReady).</summary>
    private ConnectionHandle? _activeHandle;

    /// <summary>Requesters waiting for a ConnectionHandle (queued when no active handle exists).</summary>
    private readonly List<IActorRef> _pendingHandleRequesters = [];

    public HostPoolActor(HostPoolConfig config)
    {
        _options = config.Options;
        _config = config.Config;
        _key = config.Key;

        Receive<ConnectionIdle>(HandleIdle);
        Receive<ConnectionFailed>(HandleFailure);
        Receive<IdleCheck>(_ => EvictIdleConnections());
        Receive<Reconnect>(HandleReconnect);
        Receive<MarkConnectionNoReuse>(HandleMarkNoReuse);
        Receive<ConnectionActor.ConnectionReady>(HandleConnectionReady);
        Receive<PoolRouterActor.EnsureHost>(HandleEnsureHost);
    }

    protected override void PreStart()
    {
        _scheduler = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
            _config.IdleCheckInterval,
            _config.IdleCheckInterval,
            Self,
            new IdleCheck(),
            Self);

        // Eagerly establish the first connection
        SpawnConnection();
    }

    protected override void PostStop()
    {
        _scheduler?.Cancel();
    }

    // ── ConnectionHandle forwarding ───────────────────────────────────

    private void HandleConnectionReady(ConnectionActor.ConnectionReady msg)
    {
        _activeHandle = msg.Handle;

        // Flush all pending requesters
        foreach (var requester in _pendingHandleRequesters)
        {
            requester.Tell(msg.Handle);
        }

        _pendingHandleRequesters.Clear();
    }

    private void HandleEnsureHost(PoolRouterActor.EnsureHost msg)
    {
        // If we already have an active handle, reply immediately
        if (_activeHandle is not null)
        {
            Sender.Tell(_activeHandle);
            return;
        }

        // Otherwise queue the requester — they'll be served when ConnectionReady arrives
        _pendingHandleRequesters.Add(Sender);
    }

    // ── Connection lifecycle ──────────────────────────────────────────

    private ConnectionState SpawnConnection()
    {
        var clientManager = Context.GetActor<ClientManager>();
        var actor = Context.ActorOf(Props.Create(() => new ConnectionActor(_options, clientManager, _key)));

        Context.Watch(actor);

        var state = new ConnectionState(actor);
        _connections.Add(state);

        return state;
    }

    private void HandleIdle(ConnectionIdle msg)
    {
        var conn = Find(msg.Connection);
        conn?.MarkIdle();
    }

    private void HandleFailure(ConnectionFailed msg)
    {
        var conn = Find(msg.Connection);

        if (conn == null)
        {
            return;
        }

        conn.MarkDead();

        Context.System.Scheduler.ScheduleTellOnceCancelable(
            _config.ReconnectInterval,
            Self,
            new Reconnect(msg.Connection),
            Self);
    }

    private void HandleReconnect(Reconnect msg)
    {
        var conn = Find(msg.Connection);

        if (conn == null)
        {
            return;
        }

        var previousVersion = conn.HttpVersion;
        _connections.Remove(conn);

        var newConn = SpawnConnection();
        newConn.HttpVersion = previousVersion;
    }

    private void EvictIdleConnections()
    {
        var now = DateTime.UtcNow;

        foreach (var conn in _connections.ToArray())
        {
            if (!conn.Idle)
            {
                continue;
            }

            if (now - conn.LastActivity > _config.IdleTimeout && _connections.Count > 1)
            {
                Context.Unwatch(conn.Actor);
                conn.Actor.Tell(PoisonPill.Instance);
                _connections.Remove(conn);
            }
        }
    }

    private void HandleMarkNoReuse(MarkConnectionNoReuse msg)
    {
        var conn = Find(msg.Connection);
        conn?.MarkNoReuse();
    }

    private ConnectionState? Find(IActorRef actor)
        => _connections.Find(x => x.Actor.Equals(actor));
}
