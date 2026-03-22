using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.Transport;

namespace TurboHttp.Pooling;

public sealed class ConnectionActor : ReceiveActor
{
    /// <summary>
    /// Sent to the parent actor when a TCP connection is established,
    /// providing direct Channel-based I/O access via <see cref="ConnectionHandle"/>.
    /// </summary>
    public sealed record ConnectionReady(ConnectionHandle Handle);

    /// <summary>
    /// Sent by HostPool to request a new QUIC stream on an existing connection.
    /// The requester receives a <see cref="ConnectionReady"/> with a stream-specific handle.
    /// </summary>
    public sealed record OpenNewStream(IActorRef Requester);

    /// <summary>
    /// Internal message used to trigger a scheduled reconnect attempt.
    /// </summary>
    private sealed record DoReconnect;

    /// <summary>
    /// Internal message carrying the result of opening a new QUIC stream.
    /// </summary>
    private sealed record StreamOpened(IActorRef Requester, ClientRunner.ClientConnected Connected);

    private readonly TcpOptions _options;
    private readonly IActorRef _clientManager;
    private readonly RequestEndpoint _requestEndpoint;
    private readonly TurboClientOptions _config;
    private readonly bool _isMultiStream;

    private Channel<(IMemoryOwner<byte>, int)> _out = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
    private Channel<(IMemoryOwner<byte>, int)> _in = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

    private readonly ILoggingAdapter _log = Context.GetLogger();

    private IActorRef? _runner;
    private int _reconnectAttempt;

    /// <summary>
    /// For QUIC: the shared provider that all streams on this connection use.
    /// Null for TCP/TLS connections.
    /// </summary>
    private IClientProvider? _sharedProvider;

    /// <summary>
    /// For QUIC: tracks all active runners so they can be stopped on connection teardown.
    /// </summary>
    private readonly List<IActorRef> _activeRunners = [];

    /// <summary>
    /// For QUIC: requesters waiting for a stream while the initial connection is being established.
    /// </summary>
    private readonly Queue<IActorRef> _pendingStreamRequesters = new();

    public ConnectionActor(TcpOptions options, IActorRef clientManager, RequestEndpoint requestEndpoint, TurboClientOptions config)
    {
        _options = options;
        _clientManager = clientManager;
        _requestEndpoint = requestEndpoint;
        _config = config;
        _isMultiStream = options is QuicOptions;

        Receive<ClientRunner.ClientConnected>(HandleConnected);
        Receive<ClientRunner.ClientDisconnected>(HandleDisconnected);
        Receive<Terminated>(HandleTerminated);
        Receive<DoReconnect>(_ => AttemptReconnect());
        Receive<OpenNewStream>(HandleOpenNewStream);
        Receive<HostPool.MarkConnectionNoReuse>(msg => Context.Parent.Tell(msg));
        Receive<HostPool.StreamCompleted>(msg => Context.Parent.Tell(msg));
        Receive<HostPool.StreamAcquired>(msg => Context.Parent.Tell(msg));
        Receive<HostPool.UpdateMaxConcurrentStreams>(msg => Context.Parent.Tell(msg));
    }

    protected override void PreStart()
    {
        Connect();
    }

    private void Connect()
    {
        if (_isMultiStream)
        {
            // For QUIC: create and store the shared provider so subsequent streams reuse it.
#pragma warning disable CA1416 // QuicClientProvider is guarded by QuicOptions at runtime
            _sharedProvider = new QuicClientProvider((QuicOptions)_options);
#pragma warning restore CA1416
            _clientManager.Tell(new ClientManager.CreateRunnerWithChannels(_options, Self, _out, _in, _sharedProvider));
        }
        else
        {
            _clientManager.Tell(new ClientManager.CreateRunnerWithChannels(_options, Self, _out, _in));
        }
    }

    private void HandleConnected(ClientRunner.ClientConnected msg)
    {
        _log.Debug("Connected {0}", msg.RemoteEndPoint);

        _runner = Sender;
        _reconnectAttempt = 0;

        Context.Watch(_runner);

        if (_isMultiStream)
        {
            _activeRunners.Add(_runner);
        }

        // Send ConnectionReady with direct channel handles to parent
        var handle = new ConnectionHandle(msg.OutboundWriter, msg.InboundReader, _requestEndpoint, Self);
        Context.Parent.Tell(new ConnectionReady(handle));

        // Flush any pending stream requesters that arrived before initial connection was ready
        if (_isMultiStream)
        {
            FlushPendingStreamRequesters();
        }
    }

    private void HandleOpenNewStream(OpenNewStream msg)
    {
        if (!_isMultiStream)
        {
            _log.Warning("OpenNewStream received on non-QUIC connection — ignoring");
            return;
        }

        if (_sharedProvider is null)
        {
            // Connection not yet established — queue the requester
            _pendingStreamRequesters.Enqueue(msg.Requester);
            return;
        }

        SpawnStreamRunner(msg.Requester);
    }

    private void SpawnStreamRunner(IActorRef requester)
    {
        var streamOut = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var streamIn = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

        // Spawn a new runner with the shared QUIC provider — opens a new bidirectional stream
        _clientManager.Tell(new ClientManager.CreateRunnerWithChannels(
            _options, Self, streamOut, streamIn, _sharedProvider));

        // We need to associate the requester with the upcoming ClientConnected message.
        // Use BecomeStacked to temporarily intercept the next ClientConnected for this stream.
        BecomeStacked(message =>
        {
            if (message is ClientRunner.ClientConnected connected)
            {
                UnbecomeStacked();

                var runner = Sender;
                Context.Watch(runner);
                _activeRunners.Add(runner);

                var handle = new ConnectionHandle(connected.OutboundWriter, connected.InboundReader, _requestEndpoint, Self);
                requester.Tell(handle);

                // Process any messages that may have queued
                return;
            }

            if (message is ClientRunner.ClientDisconnected disconnected)
            {
                UnbecomeStacked();
                // Stream failed to open — notify requester via parent
                _log.Warning("QUIC stream failed to open for {0}", disconnected.RemoteEndPoint);
                return;
            }

            // For any other message, use default handling
            UnbecomeStacked();

            // Re-process the message with normal handlers
            OnReceive(message);
        });
    }

    private void FlushPendingStreamRequesters()
    {
        while (_pendingStreamRequesters.Count > 0)
        {
            var requester = _pendingStreamRequesters.Dequeue();
            SpawnStreamRunner(requester);
        }
    }

    private void HandleDisconnected(ClientRunner.ClientDisconnected msg)
    {
        _log.Warning("Disconnected {0}", msg.RemoteEndPoint);

        if (_isMultiStream)
        {
            _activeRunners.Remove(Sender);

            // For QUIC: only reconnect if ALL runners are gone (connection-level failure)
            if (_activeRunners.Count > 0)
            {
                return;
            }
        }

        Reconnect();
    }

    private void HandleTerminated(Terminated msg)
    {
        if (_isMultiStream)
        {
            _activeRunners.Remove(msg.ActorRef);

            if (msg.ActorRef.Equals(_runner))
            {
                _runner = null;
            }

            // Only reconnect if all runners are gone
            if (_activeRunners.Count > 0)
            {
                return;
            }

            _log.Warning("All QUIC stream runners terminated");
            Reconnect();
            return;
        }

        if (!msg.ActorRef.Equals(_runner))
        {
            return;
        }

        _log.Warning("ClientRunner terminated");
        Reconnect();
    }

    private void Reconnect()
    {
        _runner = null;

        // Complete both channels so old pump tasks exit cleanly before new channels are created.
        _in.Writer.TryComplete();
        _out.Writer.TryComplete();

        if (_isMultiStream)
        {
            // Dispose the shared QUIC provider — the connection is dead
            _sharedProvider?.DisposeAsync().AsTask().ContinueWith(
                t => { if (t.IsFaulted) _log.Warning(t.Exception, "Failed to dispose QUIC provider"); },
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            _sharedProvider = null;
            _activeRunners.Clear();
        }

        // Notify parent of connection failure
        Context.Parent.Tell(new HostPool.ConnectionFailed(Self));

        if (_reconnectAttempt >= _config.MaxReconnectAttempts)
        {
            _log.Warning("Max reconnect attempts ({0}) reached for {1}:{2} — giving up",
                _config.MaxReconnectAttempts, _options.Host, _options.Port);
            return;
        }

        // Exponential backoff: base * 2^attempt (capped at 30s)
        var delay = TimeSpan.FromTicks(
            Math.Min(
                _config.ReconnectInterval.Ticks * (1L << _reconnectAttempt),
                TimeSpan.FromSeconds(30).Ticks));

        _reconnectAttempt++;

        _log.Debug("Scheduling reconnect attempt {0}/{1} in {2}",
            _reconnectAttempt, _config.MaxReconnectAttempts, delay);

        Context.System.Scheduler.ScheduleTellOnceCancelable(delay, Self, new DoReconnect(), Self);
    }

    private void AttemptReconnect()
    {
        // Create fresh channels — the previous _in.Writer was completed to signal stale handles.
        _out = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        _in = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        Connect();
    }

    protected override void PostStop()
    {
        if (_isMultiStream)
        {
            // Stop all active QUIC stream runners
            foreach (var runner in _activeRunners)
            {
                try
                {
                    runner.Tell(new DoClose());
                }
                catch
                {
                    // noop
                }
            }

            // Dispose the shared provider
            _sharedProvider?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _sharedProvider = null;
            return;
        }

        try
        {
            _runner?.Tell(new DoClose());
        }
        catch
        {
            // noop
        }
    }
}