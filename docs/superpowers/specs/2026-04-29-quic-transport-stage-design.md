# QUIC Transport Stage — Clean Rewrite Design

## Goal

Build a new protocol-agnostic QUIC transport stage as `Flow<ITransportOutbound, ITransportInbound>` in `Servus.Akka.Transport.Quic`. Replaces the current `QuicConnectionStage` which is deeply entangled with HTTP/3 concepts (`ProtocolReadyItem`, `OpenTypedStreamItem`, `RoutedNetworkBuffer`, `StreamFinishedItem`, stream type routing). The new stage handles generic multiplexed stream I/O by stream ID and direction only. HTTP/3 semantics (typed streams, protocol readiness, stream type bytes) move entirely to the TurboHTTP engine layer.

## Scope

**In scope:**
- New `QuicConnectionStage` as `Flow<ITransportOutbound, ITransportInbound>`
- New `QuicTransportStateMachine` — protocol-agnostic, testable via `ITransportOperations`
- Generic stream multiplexing by `long StreamId` + `StreamDirection` only
- `QuicPumpManager` with direct stream I/O (no Pipe+Channel)
- Behavior-oriented `QuicConnectionHandle`, `StreamHandle`, `StreamContext`
- Both leases (`QuicConnectionLease`, stream handles) internal to stage
- Connection migration signaled to engine, engine decides policy
- 0-RTT rejection as generic transport event
- Opt-in auto-reconnect via `QuicTransportOptions.AutoReconnect`
- Accept loop passes raw stream + ID without reading type bytes

**Out of scope:**
- TCP transport stage rewrite (separate spec)
- TurboHTTP engine-layer changes (typed stream mapping, protocol readiness — separate follow-up)
- Unified multiplexed transport abstraction (premature — QUIC is the only multiplexed protocol)

## Message Types

### Outbound (engine → transport): `ITransportOutbound`

| Message | Purpose |
|---|---|
| `ConnectTransport(TransportOptions)` | Initiate QUIC connection |
| `MultiplexedData(TransportBuffer, long StreamId)` | Send bytes to a specific stream |
| `OpenStream(long StreamId, StreamDirection)` | Open a new stream (bidirectional or unidirectional) |
| `CloseStream(long StreamId)` | Close a stream |
| `DisconnectTransport(DisconnectReason)` | Explicit disconnect |

### Inbound (transport → engine): `ITransportInbound`

| Message | Purpose |
|---|---|
| `TransportConnected(ConnectionInfo)` | QUIC connection established |
| `MultiplexedData(TransportBuffer, long StreamId)` | Received bytes from a specific stream |
| `StreamOpened(long StreamId, StreamDirection)` | Stream ready (outbound confirmed or inbound accepted) |
| `StreamClosed(long StreamId, DisconnectReason)` | Stream ended |
| `TransportDisconnected(DisconnectReason)` | Connection closed |
| `TransportError(Exception, Fatal)` | Error event |
| `DataRejected(TransportBuffer)` | 0-RTT early data rejected by server |
| `ConnectionMigrationDetected(EndPoint Old, EndPoint New)` | Migration detected, engine decides policy |

### MultiplexedData record

```csharp
public sealed record MultiplexedData(TransportBuffer Buffer, long StreamId) : ITransportOutbound, ITransportInbound;
```

QUIC uses `MultiplexedData`, TCP uses `TransportData`. Clean split, no optional fields.

### New inbound messages

```csharp
public sealed record DataRejected(TransportBuffer Buffer) : ITransportInbound;
public sealed record ConnectionMigrationDetected(EndPoint OldEndPoint, EndPoint NewEndPoint) : ITransportInbound;
```

### Connection migration policy

Transport detects migration and pushes `ConnectionMigrationDetected` to the engine. The stage continues normally — migration is implicitly accepted. If the engine wants to reject the migration, it sends `DisconnectTransport`. No response message needed.

### Shared messages with TCP

`ConnectTransport`, `DisconnectTransport`, `TransportConnected`, `TransportDisconnected`, `TransportError`, `OpenStream`, `StreamOpened`, `StreamClosed` — identical types used by both TCP and QUIC stages.

## Stage Shape

```csharp
public sealed class QuicConnectionStage : GraphStage<FlowShape<ITransportOutbound, ITransportInbound>>
{
    public QuicConnectionStage(IActorRef connectionManager)
}
```

No `IPoolingStrategy` — QUIC gets everything from `QuicTransportOptions`. No `allowConnectionMigration` — engine decides via the event.

Port naming:
- Inlet: `QuicConnection.In`
- Outlet: `QuicConnection.Out`

## GraphStageLogic

Thin adapter between Akka.Streams and the state machine — same pattern as TCP:

```csharp
private sealed class Logic : TimerGraphStageLogic, ITransportOperations
{
    private readonly QuicConnectionStage _stage;
    private readonly Queue<ITransportInbound> _pendingReads = new();
    private QuicTransportStateMachine _sm = null!;
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
- Instantiate `QuicTransportStateMachine(this, connectionManager, stageActor.Ref)`
- `Pull(_in)` to start demand

**StageActor `OnReceive`:**
- Pattern match on `IQuicTransportEvent` → `_sm.Dispatch(evt)`

**`ITransportOperations` implementation:** Identical to TCP — `OnPushInbound`, `OnSignalPullOutbound`, `OnCompleteStage`, timer methods. Shared interface, two implementations.

## State Machine

### ITransportOperations callback interface (shared with TCP)

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

### QuicTransportStateMachine

```csharp
public sealed class QuicTransportStateMachine
{
    public QuicTransportStateMachine(
        ITransportOperations ops,
        IActorRef connectionManager,
        IActorRef self)

    public void HandlePush(ITransportOutbound item)
    public void HandleUpstreamFinish()
    public void HandleDownstreamFinish()
    public void Dispatch(IQuicTransportEvent evt)
    public void OnTimer(string? timerKey)
    public void PostStop()
}
```

### HandlePush dispatch

Only transport messages — no HTTP/3:

| Message | Action |
|---|---|
| `ConnectTransport` | Cleanup existing, acquire QUIC connection via ConnectionManager |
| `OpenStream(streamId, direction)` | Open stream on connection handle, register in stream map |
| `MultiplexedData(buffer, streamId)` | Route to StreamContext — write or enqueue pending |
| `CloseStream(streamId)` | Close stream, remove from stream map |
| `DisconnectTransport` | Cleanup transport, return connection lease |

### Internal state

```
_connectionHandle: QuicConnectionHandle?                    — current QUIC connection
_connectionLease: QuicConnectionLease?                      — active connection lease
_connectionGen: int                                         — generation counter
_pendingConnect: ConnectTransport?                          — queued connect request
_streams: Dictionary<long, StreamContext>                   — per-stream state
_pendingStreamOpens: Queue<(long, StreamDirection)>         — queued before connection ready
_autoReconnect: bool                                        — from QuicTransportOptions
```

### StreamContext (behavior-oriented)

```csharp
internal sealed class StreamContext
{
    public StreamContext(StreamDirection direction)

    public bool HasHandle()
    public void AttachHandle(StreamHandle handle)
    public void Write(TransportBuffer buffer)
    public bool TryDequeuePendingWrite(out TransportBuffer buffer)
    public void CompleteWrites()
    public StreamDirection Direction()
}
```

No public fields. Write enqueues if handle not attached, writes directly if attached.

### Key differences from TCP state machine

- No `_pendingWrites` at connection level — writes are per-stream in `StreamContext`
- No `IPoolingStrategy` — connection return logic uses `QuicTransportOptions` directly
- Stream map (`_streams`) replaces TCP's single-handle model
- Accept loop manages server-initiated streams
- Migration detection delegates to engine

## Internal Events

`IQuicTransportEvent` — async events dispatched through StageActor:

| Event | Purpose |
|---|---|
| `ConnectionLeaseAcquired(QuicConnectionLease)` | Pool returned a QUIC connection |
| `StreamLeaseAcquired(StreamHandle, long StreamId)` | Stream opened, handle ready |
| `AcquisitionFailed(Exception)` | Connection or stream acquisition failed |
| `InboundData(TransportBuffer, long StreamId, int Gen)` | Data received on a stream |
| `InboundStreamAccepted(Stream, long StreamId)` | Server-initiated stream accepted (raw, engine reads type) |
| `InboundComplete(DisconnectReason, int Gen, long StreamId)` | Stream read finished |
| `InboundPumpFailed(Exception, long StreamId)` | Stream pump crashed |
| `OutboundWriteDone(long StreamId)` | Write to stream completed |
| `OutboundWriteFailed(Exception, long StreamId)` | Write to stream failed |
| `MigrationDetected(EndPoint Old, EndPoint New)` | Local endpoint changed |
| `EarlyDataRejected(TransportBuffer)` | 0-RTT data rejected by server |

Generation counter `Gen` on inbound events for stale filtering — same pattern as TCP.

## QuicPumpManager

Manages per-stream inbound pumps and the accept loop. Direct stream I/O — no Pipe+Channel (QUIC has protocol-level flow control).

```csharp
public sealed class QuicPumpManager
{
    public QuicPumpManager(IActorRef stageActor)

    public void StartInboundPump(StreamHandle handle, long streamId, int gen)
    public void StartAcceptLoop(QuicConnectionHandle connectionHandle)
    public void StopAll()
}
```

### Per-stream inbound pump

1. Reads from `StreamHandle` directly into rented `TransportBuffer`
2. Sends `InboundData(buffer, streamId, gen)` to StageActor
3. On stream end → sends `InboundComplete(DisconnectReason, gen, streamId)`
4. On exception → sends `InboundPumpFailed(exception, streamId)`

### Accept loop

1. Calls `connectionHandle.AcceptInboundStreamAsync()` in a loop
2. Gets raw `Stream` + `streamId` from QUIC connection
3. Sends `InboundStreamAccepted(stream, streamId)` to StageActor
4. State machine creates `StreamContext`, wraps stream in `StreamHandle`, starts inbound pump

### Outbound writes

State machine calls `streamContext.Write(buffer)`, which writes directly to `StreamHandle`. Write completion/failure sent as `OutboundWriteDone(streamId)` / `OutboundWriteFailed(ex, streamId)` via PipeTo to StageActor.

No batching on inbound — each read is one event. QUIC flow control handles backpressure at the protocol level.

## QuicConnectionHandle

Behavior-oriented — methods only, no exposed internals:

```csharp
public sealed class QuicConnectionHandle : IAsyncDisposable
{
    public Task<(Stream Stream, long StreamId)> OpenStreamAsync(StreamDirection direction, CancellationToken ct)
    public Task<(Stream Stream, long StreamId)?> AcceptInboundStreamAsync(CancellationToken ct)
    public EndPoint? LocalEndPoint()
    public ValueTask DisposeAsync()
}
```

- `OpenStreamAsync` — opens bidirectional or unidirectional stream, returns raw stream + QUIC stream ID
- `AcceptInboundStreamAsync` — accepts server-initiated stream, returns raw stream + ID, no type byte reading
- `LocalEndPoint()` — for migration detection
- No `RequestEndpoint`, no `QuicOptions` exposed

## StreamHandle

Behavior-oriented per-stream I/O:

```csharp
public sealed class StreamHandle : IAsyncDisposable
{
    public ValueTask WriteAsync(TransportBuffer buffer)
    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    public void CompleteWrites()
    public ValueTask DisposeAsync()
}
```

- No `RequestEndpoint Key` — protocol-agnostic
- `WriteAsync` takes `TransportBuffer` instead of `NetworkBuffer`
- `CompleteWrites` signals write-side FIN

## Connection Leases (internal)

**`QuicConnectionLease`** — internal, engine never sees it:

```csharp
internal sealed class QuicConnectionLease : IDisposable
{
    public QuicConnectionHandle Handle { get; }
    public bool IsAlive()
    public bool IsExpired(TimeSpan lifetime)
    public bool CanAcceptStream()
    public void MarkBusy()
    public void MarkIdle()
    public void Dispose()
}
```

No `QuicStreamLease` — stream lifecycle tracked in `StreamContext`, `StreamHandle` disposed by state machine cleanup.

## ConnectionManager

```csharp
public sealed class QuicConnectionManagerActor : ReceiveActor, IWithTimers
{
    public sealed record Acquire(QuicTransportOptions Options, TaskCompletionSource<QuicConnectionLease> Tcs, CancellationToken Token);
    public sealed record Release(QuicConnectionLease Lease, bool CanReuse);
}
```

**Changes from current:**
- `Acquire` takes `QuicTransportOptions` instead of `(QuicOptions, RequestEndpoint)`
- Pool keyed by `QuicTransportOptions` instead of `RequestEndpoint`
- `MaxConnectionsPerHost`, `IdleTimeout`, `ConnectionLifetime`, `MaxConcurrentStreams` read from `QuicTransportOptions`

## IQuicConnectionFactory

```csharp
public interface IQuicConnectionFactory
{
    Task<QuicConnectionLease> EstablishAsync(QuicTransportOptions options, CancellationToken ct);
}
```

No `RequestEndpoint` parameter — `QuicTransportOptions` carries host, port, TLS, timeouts.

## Transport Options

```csharp
public sealed record QuicTransportOptions : TransportOptions
{
    public string Host { get; init; }
    public int Port { get; init; }
    public TimeSpan ConnectTimeout { get; init; }
    public int MaxBidirectionalStreams { get; init; }
    public int MaxUnidirectionalStreams { get; init; }
    public int MaxConnectionsPerHost { get; init; }
    public TimeSpan IdleTimeout { get; init; }
    public TimeSpan ConnectionLifetime { get; init; }
    public bool AutoReconnect { get; init; }
}
```

Pool configuration lives in the options — no separate `IPoolingStrategy`.

## Auto-Reconnect

Opt-in via `QuicTransportOptions.AutoReconnect` (default: `false`).

**When enabled:**
1. Connection-level failure → SM pushes `TransportDisconnected(Transient)`
2. SM closes all active streams, pushes `StreamClosed` for each
3. SM re-acquires via ConnectionManager
4. On success → pushes `TransportConnected(info)`
5. Engine must re-open streams and resend data — transport doesn't replay

**When disabled:**
1. Connection-level failure → SM pushes `TransportDisconnected(reason)`
2. SM closes all streams, cleans up
3. Engine decides whether to send new `ConnectTransport`

## Connection Lifecycle

```
Engine sends ConnectTransport(QuicTransportOptions)
  → SM: AcquireConnection via ConnectionManager
  → SM: ScheduleTimer(connect-timeout)

ConnectionManager replies ConnectionLeaseAcquired(lease)
  → SM: CancelTimer, store connection handle/lease
  → SM: push TransportConnected(info)
  → SM: open any pending streams from _pendingStreamOpens

Engine sends OpenStream(streamId, direction)
  → SM: if connected → connectionHandle.OpenStreamAsync(direction)
  → SM: if not connected → enqueue in _pendingStreamOpens

StreamLeaseAcquired(handle, streamId)
  → SM: attach handle to StreamContext
  → SM: start inbound pump for this stream
  → SM: flush pending writes
  → SM: push StreamOpened(streamId, direction) to engine

Engine sends MultiplexedData(buffer, streamId)
  → SM: streamContext.Write(buffer)

Inbound pump delivers InboundData(buffer, streamId, gen)
  → SM: push MultiplexedData(buffer, streamId) to engine

Stream ends (InboundComplete or OutboundWriteFailed)
  → SM: push StreamClosed(streamId, reason) to engine
  → SM: remove stream from _streams map
  → SM: dispose StreamHandle

Connection-level failure
  → SM: push TransportDisconnected(reason)
  → SM: close all streams, push StreamClosed for each
  → SM: if autoReconnect → re-acquire, push TransportDisconnected(Transient)
  → SM: on reconnect success → push TransportConnected(info)

Accept loop delivers InboundStreamAccepted(stream, streamId)
  → SM: create StreamContext, wrap stream in StreamHandle
  → SM: start inbound pump
  → SM: push StreamOpened(streamId, Inbound) to engine

Migration detected
  → SM: push ConnectionMigrationDetected(old, new) to engine
  → SM: continue normally (engine sends DisconnectTransport if it wants to abort)

0-RTT rejected
  → SM: push DataRejected(buffer) to engine
```

## File Structure

### New files in `Servus.Akka.Transport.Quic`

| File | Type | Purpose |
|---|---|---|
| `QuicConnectionStage.cs` | GraphStage + Logic | `Flow<ITransportOutbound, ITransportInbound>`, thin adapter |
| `QuicTransportStateMachine.cs` | Class | Connection lifecycle, stream map, reconnect |
| `QuicPumpManager.cs` | Class | Per-stream inbound pumps + accept loop |
| `QuicConnectionManagerActor.cs` | Actor | Per-host connection pool |
| `QuicConnectionFactory.cs` | `IQuicConnectionFactory` impl | Establishes QUIC connections |
| `QuicTransportFactory.cs` | `ITransportFactory` impl | Creates `QuicConnectionStage` |
| `QuicTransportEvent.cs` | Internal events | `IQuicTransportEvent` hierarchy |
| `QuicConnectionHandle.cs` | Class | Behavior-oriented connection (OpenStream, AcceptInbound) |
| `StreamHandle.cs` | Class | Behavior-oriented per-stream I/O (Write, Read, CompleteWrites) |
| `StreamContext.cs` | Internal class | Per-stream state (behavior-oriented) |
| `QuicClientProvider.cs` | Class | .NET QUIC connection establishment |

### Deleted files (replaced)

- `QuicStreamRouter.cs` — routing simplified into state machine's stream map
- `QuicStreamLease.cs` — replaced by `StreamContext` + `StreamHandle`
- `QuicConnectionLease.cs` — rewritten, internal
- `TypedStreamDescriptor.cs` — HTTP/3 concept, moves to TurboHTTP
- `StreamDirection.cs` (IO.Quic) — replaced by `Transport.StreamDirection` + `Transport.PipeMode`
- `IQuicTransportEvent.cs` — rewritten in `QuicTransportEvent.cs`

### Updated shared types in `Servus.Akka.Transport`

| File | Changes |
|---|---|
| `ITransportInbound.cs` | Add `MultiplexedData`, `DataRejected`, `ConnectionMigrationDetected` |
| `ITransportOutbound.cs` | Add `MultiplexedData` |

### Moves to TurboHTTP

- `TypedStreamDescriptor` / typed stream concept — HTTP/3 engine layer
- `RoutedNetworkBuffer` — replaced by engine wrapping `MultiplexedData` with stream type mapping
- `ProtocolReadyItem`, `OpenTypedStreamItem` — HTTP/3 engine signals

## Success Criteria

- `dotnet build --configuration Release ./src/TurboHTTP.slnx` compiles with zero errors
- All unit tests pass
- All stream tests pass
- Integration tests pass (run per-namespace)
- No HTTP/3-specific messages in transport layer
- No `RequestEndpoint` in transport layer
- No stream type routing in transport layer
- `QuicTransportStateMachine` testable without Akka infrastructure
- Accept loop delivers raw streams without reading type bytes
