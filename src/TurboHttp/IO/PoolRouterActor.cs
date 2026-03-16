using System;
using System.Collections.Generic;
using Akka.Actor;
using TurboHttp.Client;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class PoolRouterActor : ReceiveActor
{
    // ── Public message protocol ───────────────────────────────────────

    /// <summary>
    /// Sent by ConnectionStage on each ConnectItem to ensure a HostPoolActor exists.
    /// The message is forwarded to the HostPoolActor so it can reply with a ConnectionHandle.
    /// </summary>
    public sealed record EnsureHost(HostKey Key, TcpOptions Options);

    // ── Fields ────────────────────────────────────────────────────────

    private readonly TurboClientOptions _config;
    private readonly Func<TcpOptions, TurboClientOptions, HostKey, IActorRef> _hostFactory;
    private readonly Dictionary<HostKey, IActorRef> _hosts = new();

    public PoolRouterActor(TurboClientOptions config,
        Func<TcpOptions, TurboClientOptions, HostKey, IActorRef>? hostFactory = null)
    {
        _config = config;
        _hostFactory = hostFactory ?? CreateHostPoolActor;

        Receive<EnsureHost>(HandleEnsureHost);
    }

    // ── Message handlers ──────────────────────────────────────────────

    private void HandleEnsureHost(EnsureHost msg)
    {
        var hostActor = EnsureHostActor(msg.Key, msg.Options);

        // Forward preserves the original Sender so HostPoolActor can reply directly.
        hostActor.Forward(msg);
    }

    private IActorRef EnsureHostActor(HostKey key, TcpOptions options)
    {
        if (!_hosts.TryGetValue(key, out var hostActor))
        {
            hostActor = _hostFactory(options, _config, key);
            _hosts[key] = hostActor;
        }

        return hostActor;
    }

    private IActorRef CreateHostPoolActor(TcpOptions options, TurboClientOptions config, HostKey key)
    {
        var hostConfig = new HostPoolActor.HostPoolConfig(options, config, key);
        return Context.ActorOf(Props.Create(() => new HostPoolActor(hostConfig)), Guid.NewGuid().ToString());
    }
}