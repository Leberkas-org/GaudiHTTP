# TCP Transport Stage — Clean Rewrite Design

## Goal

Build a new protocol-agnostic TCP transport stage as `Flow<ITransportOutbound, ITransportInbound>` in `Servus.Akka.Transport.Tcp`. Replaces the current `TcpConnectionStage` which mixes HTTP-specific messages (`MaxConcurrentStreamsItem`, `StreamAcquireItem`, `ConnectionReuseItem`) into the transport layer. The new stage speaks only transport messages and delegates all protocol-specific concerns to the engine layer.

## Scope

**In scope:**
- New `TcpConnectionStage` as `Flow<ITransportOutbound, ITransportInbound>`
- New `TcpTransportStateMachine` — protocol-agnostic, testable via `ITransportOperations`
- `TcpPumpManager` owns bidirectional Pipe+Channel pump lifecycle (start/stop only)
- Behavior-oriented `ConnectionHandle` (methods, not properties)
- Slimmed `ConnectionLease` without HTTP pool concerns
- `IPoolingStrategy` injected into stage — replaces per-message `ConnectionReuse`
- Opt-in auto-reconnect via `TcpTransportOptions.AutoReconnect`
- Delegation to `TcpConnectionManagerActor` for connection pooling

**Out of scope:**
- QUIC transport stage rewrite (separate effort)
- TurboHTTP engine-layer changes to adapt to new transport API (separate follow-up plan)
- `RequestEndpoint` → `TransportOptions` factory mapping in TurboHTTP

## Message Types

### Outbound (engine → transport): `ITransportOutbound`

| Message | Purpose |
|---|---|
| `ConnectTransport(TransportOptions)` | Initiate connection |
| `TransportData(TransportBuffer)` | Send bytes to network |
| `DisconnectTransport(DisconnectReason)` | Explicit disconnect request |

### Inbound (transport → engine): `ITransportInbound`

| Message | Purpose |
|---|---|
| `TransportConnected(ConnectionInfo)` | Connection established |
| `TransportData(TransportBuffer)` | Received bytes from network |
| `TransportDisconnected(DisconnectReason)` | Connection closed (clean or error) |
| `TransportError(Exception, Fatal)` | Non-fatal error or fatal failure |

### TransportData record

```csharp
public sealed record TransportData(TransportBuffer Buffer) : ITransportOutbound, ITransportInbound;
```

Wraps `TransportBuffer` on both sides. The buffer itself does not implement the marker interfaces.

### DisconnectReason

```csharp
public enum DisconnectReason
{
    Graceful,   // clean close / FIN
    Error,      // abrupt close / RST / I/O error
    Timeout,    // connect or idle timeout
    Transient   // auto-reconnect in progress
}
```

## Stage Shape

```csharp
public sealed class TcpConnectionStage : GraphStage<FlowShape<ITransportOutbound, ITransportInbound>>
{
    public TcpConnectionStage(
        IActorRef connectionManager,
        IPoolingStrategy poolingStrategy)
}
```

Auto-reconnect is controlled per-connection via `TcpTransportOptions.AutoReconnect`, not at stage construction time.

Port naming:
- Inlet: `TcpConnection.In`
- Outlet: `TcpConnection.Out`

## GraphStageLogic

Thin adapter between Akka.Streams and the state machine:

```csharp
private sealed class Logic : TimerGraphStageLogic, ITransportOperations
{
    private readonly TcpConnectionStage _stage;
    private readonly Queue<ITransportInbound> _pendingReads = new();
    private TcpTransportStateMachine _sm = null!;
}
```

**Inlet handler** (`ITransportOutbound` from engine):
- `onPush` → `_sm.HandlePush(Grab(_in))`
- `onUpstreamFinish` → `_sm.HandleUpstreamFinish()`

**Outlet handler** (`ITransportInbound` to engine):
- `onPull` → dequeue from `_pendingReads` if available, push
- `onDownstreamFinish` → `_sm.HandleDownstreamFinish()`, `CompleteStage()`

**PreStart:**
- Create `StageActor` with `OnReceive` handler
- Instantiate `TcpTransportStateMachine(this, connectionManager, poolingStrategy, stageActor.Ref)`
- `Pull(_in)` to start demand

**StageActor `OnReceive`:**
- Pattern match on `ITcpTransportEvent` → `_sm.Dispatch(evt)`

## State Machine

### ITransportOperations callback interface

```csharp
public interface ITransportOperations
{
    void OnPushInbound(ITransportInbound item);
    void OnSignalPullOutbound();
    void OnCompleteStage();
    void OnScheduleTimer(string key, TimeSpan delay);
    void OnCancelTimer(string key);
    ILoggingAdapter Log { get; }
}
```

### TcpTransportStateMachine

```csharp
public sealed class TcpTransportStateMachine
{
    public TcpTransportStateMachine(
        ITransportOperations ops,
        IActorRef connectionManager,
        IPoolingStrategy poolingStrategy,
        IActorRef self)

    public void HandlePush(ITransportOutbound item)
    public void HandleUpstreamFinish()
    public void HandleDownstreamFinish()
    public void Dispatch(ITcpTransportEvent evt)
    public void OnTimer(string? timerKey)
    public void PostStop()
}
```

### HandlePush dispatch

Only three message types — no HTTP-specific messages:

| Message | Action |
|---|---|
| `ConnectTransport` | Cleanup existing connection, acquire new via ConnectionManager |
| `TransportData` | If connected → `_handle.Write(buffer)`. If not → enqueue in `_pendingWrites` |
| `DisconnectTransport` | Cleanup transport, return lease via `IPoolingStrategy.OnRelease()` |

### Internal state

```
_handle: ConnectionHandle?          — current connection
_currentLease: ConnectionLease?     — active lease
_connectionGen: int                 — generation counter for stale event filtering
_pendingConnect: ConnectTransport?  — queued connect request
_pendingWrites: Queue<TransportBuffer> — buffered before connection ready
_upstreamFinished: bool
_isReconnecting: bool               — only used when autoReconnect is true
```

### Connection lifecycle

```
Engine sends ConnectTransport(options)
  → SM: AcquireConnection via ConnectionManager
  → SM: ScheduleTimer(connect-timeout)

ConnectionManager replies LeaseAcquired(lease)
  → SM: CancelTimer, store handle/lease, start pumps
  → SM: if reconnect → push TransportConnected(info)
  → SM: flush pending writes into handle.Write()

Engine sends TransportData(buffer)
  → SM: if connected → _handle.Write(buffer)
  → SM: if not connected → enqueue in _pendingWrites

Inbound pump delivers InboundBatch
  → SM: push TransportData(buffer) for each item

Connection closes (InboundComplete or OutboundWriteFailed)
  → SM: push TransportDisconnected(reason)
  → SM: consult IPoolingStrategy for lease return
  → SM: if autoReconnect → re-acquire; else signal upstream

Engine sends DisconnectTransport(reason)
  → SM: CleanupTransport, return lease via IPoolingStrategy.OnRelease()
```

## Internal Events

`ITcpTransportEvent` — async events dispatched through StageActor:

| Event | Purpose |
|---|---|
| `LeaseAcquired(ConnectionLease)` | Pool returned a connection |
| `AcquisitionFailed(Exception)` | Pool failed to establish connection |
| `InboundBatch(ITransportInbound[], int Count, int Gen)` | Batch of received data from pump |
| `InboundComplete(DisconnectReason, int Gen)` | Inbound stream ended |
| `InboundPumpFailed(Exception)` | Pump crashed |
| `OutboundWriteDone(int Gen)` | Outbound drain-to-stream completed |
| `OutboundWriteFailed(Exception)` | Write to socket failed |

Generation counter `Gen` is carried so the state machine can discard stale events from previous connections.

## TcpPumpManager

Lifecycle management only — no write API. Owns both inbound and outbound Pipe+Channel pump loops:

```csharp
public sealed class TcpPumpManager
{
    public TcpPumpManager(IActorRef stageActor)

    public void StartPumps(ClientState state, int gen)
    public void StopPumps()
}
```

### Inbound pipeline (socket → stage)

1. `FillPipeFromStream` — reads from `Stream` into `InboundPipe.Writer`
2. `DrainPipeToChannel` — reads from `InboundPipe.Reader`, wraps segments as `TransportData(TransportBuffer)`, writes to inbound Channel
3. Pump loop reads channel, batches into `ITransportInbound[]`, sends `InboundBatch(batch, count, gen)` to StageActor

### Outbound pipeline (stage → socket)

1. State machine calls `_handle.Write(buffer)` → pushes `TransportBuffer` into outbound Channel internally
2. `FillPipeFromChannel` — reads from outbound Channel, copies into `OutboundPipe.Writer`
3. `DrainPipeToStream` — reads from `OutboundPipe.Reader`, writes to `Stream`
4. On completion → sends `OutboundWriteDone(gen)` to StageActor
5. On failure → sends `OutboundWriteFailed(ex)` to StageActor

Backpressure flows through Pipe pause/resume thresholds. The Channel bridges async I/O into the actor messaging world.

## ConnectionHandle

Behavior-oriented — no public properties, only methods:

```csharp
public sealed class ConnectionHandle
{
    public void Write(TransportBuffer buffer)
    public bool TryRead(out TransportBuffer buffer)
    public void SignalClose()
    public bool IsCancelled { get; }
}
```

- `Write` — pushes to the outbound channel
- `TryRead` — reads from the inbound channel
- `SignalClose` — completes the outbound channel (triggers graceful close)
- `IsCancelled` — checks the CancellationToken

Internals (`ChannelWriter`, `ChannelReader`, `TransportOptions`, `CancellationToken`) are private fields.

## ConnectionLease

Slim lifecycle wrapper:

```csharp
public sealed class ConnectionLease : IDisposable
{
    public ConnectionHandle Handle { get; }
    public bool IsAlive()
    public bool IsExpired(TimeSpan lifetime)
    public void Dispose()
}
```

No HTTP concerns — no `MaxConcurrentStreams`, `ActiveStreams`, `HasAvailableSlot`, `MarkBusy()`, `MarkIdle()`. Those stay in TurboHTTP.

## ClientState

Fully internal. Same Pipe+Channel architecture, updated types:

```csharp
internal sealed class ClientState : IDisposable
{
    public Stream Stream { get; }
    public PipeMode Direction { get; }
    public Pipe InboundPipe { get; }
    public Pipe OutboundPipe { get; }
    public Channel<TransportBuffer> InboundChannel { get; }
    public Channel<TransportBuffer> OutboundChannel { get; }
}
```

## IPoolingStrategy

Full interface — injected into stage and consulted by both state machine and ConnectionManager:

```csharp
public interface IPoolingStrategy
{
    int MaxConnectionsPerHost { get; }
    TimeSpan IdleTimeout { get; }
    TimeSpan ConnectionLifetime { get; }

    bool CanReuse(TransportOptions options);
    PoolAction OnRelease(TransportOptions options);
    PoolAction OnIdle(ConnectionLease lease);
    PoolAction OnDisconnect(ConnectionLease lease, DisconnectReason reason);
    PoolAction OnUpstreamFinish(ConnectionLease lease);
}
```

- `MaxConnectionsPerHost` — ConnectionManager uses for per-host pool limits
- `IdleTimeout` / `ConnectionLifetime` — eviction timers in the pool
- `CanReuse(options)` — whether this transport type supports connection reuse at all
- `OnRelease(options)` — what to do when a lease is explicitly released
- `OnIdle`, `OnDisconnect`, `OnUpstreamFinish` — situational decisions per connection

## Auto-Reconnect

Opt-in via `TcpTransportOptions.AutoReconnect` (default: `false`).

**When enabled:**
1. `InboundComplete` or `OutboundWriteFailed` → SM pushes `TransportDisconnected(Transient)` to engine
2. SM immediately re-acquires via ConnectionManager
3. On `LeaseAcquired` → pushes `TransportConnected(info)` so the engine knows the connection is back
4. Pending writes from before the disconnect are discarded (engine must resend if needed)

**When disabled:**
1. `InboundComplete` or `OutboundWriteFailed` → pushes `TransportDisconnected(reason)`
2. SM cleans up, returns lease via `IPoolingStrategy`
3. Engine decides what to do — send a new `ConnectTransport` or finish

## Transport Options

```csharp
public sealed record TcpTransportOptions : TransportOptions
{
    public string Host { get; init; }
    public int Port { get; init; }
    public TimeSpan ConnectTimeout { get; init; }
    public int SendBufferSize { get; init; }
    public int ReceiveBufferSize { get; init; }
    public bool AutoReconnect { get; init; }
}
```

## IConnectionFactory

Simplified signature:

```csharp
public interface IConnectionFactory
{
    Task<ConnectionLease> EstablishAsync(TransportOptions options, CancellationToken ct);
}
```

No `RequestEndpoint` parameter. `TransportOptions` carries host, port, TLS, timeouts.

## File Structure

### New files in `Servus.Akka.Transport.Tcp`

| File | Type | Purpose |
|---|---|---|
| `TcpConnectionStage.cs` | GraphStage + Logic | `Flow<ITransportOutbound, ITransportInbound>`, thin adapter |
| `TcpTransportStateMachine.cs` | Class | All connection/write/read/reconnect logic |
| `TcpPumpManager.cs` | Class | Starts/stops bidirectional Pipe+Channel pump loops |
| `TcpConnectionManagerActor.cs` | Actor | Per-host connection pool, consults `IPoolingStrategy` |
| `TcpConnectionFactory.cs` | `IConnectionFactory` impl | Creates `ClientState`, launches `ClientByteMover` |
| `TcpTransportFactory.cs` | `ITransportFactory` impl | Creates `TcpConnectionStage` with dependencies |
| `TcpTransportEvent.cs` | Internal events | `ITcpTransportEvent` hierarchy |
| `TcpClientProvider.cs` | Class | Socket creation, DNS resolution, proxy support |
| `TlsClientProvider.cs` | Class | TLS/SSL wrapper over TCP |
| `DnsCache.cs` | Class | DNS resolution caching |

### Updated shared types in `Servus.Akka.Transport`

| File | Changes |
|---|---|
| `ITransportOutbound.cs` | Add `TransportData(TransportBuffer) : ITransportOutbound` |
| `ITransportInbound.cs` | Add `TransportData(TransportBuffer) : ITransportInbound` |
| `IPoolingStrategy.cs` | Full interface with MaxConnectionsPerHost, IdleTimeout, etc. |
| `ConnectionHandle.cs` | Rewrite to behavior-oriented (Write, TryRead, SignalClose) |
| `ConnectionLease.cs` | Slim down, remove HTTP concerns |
| `ClientState.cs` | Internal, `TransportBuffer` channels |
| `ClientByteMover.cs` | Same pattern, `TransportBuffer` |
| `IConnectionFactory.cs` | Simplified `(TransportOptions, CT)` |
| `ITransportFactory.cs` | `Flow<ITransportOutbound, ITransportInbound>` |

### Deleted after migration

- Entire `IO/Tcp/` folder — replaced by `Transport/Tcp/`
- `IO/Messages.cs` — transport messages in `Transport/`, HTTP messages move to TurboHTTP

## Success Criteria

- `dotnet build --configuration Release ./src/TurboHTTP.slnx` compiles with zero errors
- All unit tests pass
- All stream tests pass
- Integration tests pass (run per-namespace)
- No HTTP-specific messages in transport layer
- No `RequestEndpoint` in transport layer
- `TcpTransportStateMachine` testable without Akka infrastructure
