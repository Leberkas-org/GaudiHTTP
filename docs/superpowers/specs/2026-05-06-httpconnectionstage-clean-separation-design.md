# HttpConnectionStage Clean Separation Design

**Date:** 2026-05-06
**Status:** Approved

## Goal

Refactor all four HTTP connection stages (Http10, Http11, Http20, Http30) to a uniform pattern where the stage contains zero protocol logic — only mechanical Pull/Push/queue operations. All decision-making lives in protocol-specific StateMachines.

## Interfaces

### IHttpStateMachine

The contract each protocol SM implements. Receives `IStageOperations` via constructor — no `Init` method.

```csharp
interface IHttpStateMachine
{
    bool CanAcceptRequest { get; }

    void PreStart();
    void OnRequest(HttpRequestMessage request);
    void DecodeServerData(ITransportInbound data);
    void OnUpstreamFinished();
    void OnTimerFired(string name);
    void Cleanup();
}
```

- `DecodeServerData` receives raw `ITransportInbound` — the SM pattern-matches internally on `TransportConnected`, `TransportDisconnected`, `TransportData`, `MultiplexedData`, `StreamClosed`, etc.
- `OnUpstreamFinished` covers Akka stream upstream finish only — transport-level connect/disconnect flows through `DecodeServerData`.
- `CanAcceptRequest` is the one piece of SM state the stage reads (backpressure guard for app inlet pull).

### IStageOperations

How the SM talks back to the stage. Passed via SM constructor.

```csharp
interface IStageOperations
{
    void OnResponse(HttpResponseMessage response);
    void OnOutbound(ITransportOutbound item);
    void OnWarning(string message);
    void OnScheduleTimer(string name, TimeSpan duration);
    void OnCancelTimer(string name);
    void OnComplete();
    void OnFail(Exception exception);
    ILoggingAdapter Log { get; }
}
```

- `OnScheduleTimer` / `OnCancelTimer` — SM controls all timer lifecycle. Stage mechanically schedules/cancels.
- `OnComplete` / `OnFail` — SM decides when to shut down or fail the stage.
- `OnResponse` / `OnOutbound` — SM emits data; stage enqueues and eagerly tries to push.

## Generic Base Stage

A single `HttpConnectionStageLogic<TSM>` replaces all four per-protocol stage logic classes.

### Ports (identical for all protocols)

```
Inlet<ITransportInbound>    _inServer
Outlet<HttpResponseMessage> _outResponse
Inlet<HttpRequestMessage>   _inApp
Outlet<ITransportOutbound>  _outNetwork
```

### Stage Event Table

| Event | Stage Action |
|---|---|
| `OnPush(_inServer)` | `_sm.DecodeServerData(Grab(_inServer))` |
| `OnPush(_inApp)` | `_sm.OnRequest(Grab(_inApp))` |
| `OnPull(_outResponse)` | Dequeue from `_responseQueue`, `Push`. If empty + `CanAcceptRequest` → `TryPull(_inApp)` |
| `OnPull(_outNetwork)` | Dequeue from `_outboundQueue`, `Push`. Then `TryPull(_inServer)` |
| `OnUpstreamFinish(_inServer)` | `_sm.OnUpstreamFinished()` |
| Timer fires | `_sm.OnTimerFired(name)` |
| `PostStop` | `_sm.Cleanup()`, dispose queued buffers |

### IStageOperations Implementation

| Callback | Stage Action |
|---|---|
| `OnResponse(r)` | `_responseQueue.Enqueue(r)`, `TryPush(_outResponse)` |
| `OnOutbound(item)` | `_outboundQueue.Enqueue(item)`, `TryPush(_outNetwork)` |
| `OnScheduleTimer(name, dur)` | `ScheduleOnce(name, dur)` |
| `OnCancelTimer(name)` | `CancelTimer(name)` |
| `OnComplete()` | `CompleteStage()` |
| `OnFail(ex)` | `FailStage(ex)` |
| `OnWarning(msg)` | `Log.Warning(msg)` |

The stage is purely a pump: enqueue on callback, dequeue on pull, forward events to SM. No `if` branches based on protocol state.

### Construction

```csharp
HttpConnectionStageLogic(
    GraphStage stage,
    Func<IStageOperations, TSM> smFactory)
{
    // ... set up ports, queues ...
    _sm = smFactory(this);  // stage IS the IStageOperations
}
```

### Per-Protocol Outer Stage

Each protocol keeps a thin outer `GraphStage` class for pipeline composition. It holds protocol-specific config and passes a factory delegate:

```csharp
sealed class Http11ConnectionStage : GraphStage<...>
{
    readonly HttpConnectionSettings _settings;

    protected override GraphStageLogic CreateLogic(Attributes attrs)
    {
        return new HttpConnectionStageLogic<Http11StateMachine>(
            this,
            ops => new Http11StateMachine(ops, _settings)
        );
    }
}
```

## StateMachine Internal Structure

Each SM handles all protocol logic via pattern matching on `ITransportInbound`.

### Http10/Http11 Pattern

```csharp
public void DecodeServerData(ITransportInbound data)
{
    switch (data)
    {
        case TransportConnected:
            OnConnected();
            break;
        case TransportDisconnected:
            OnDisconnected();
            break;
        case TransportData { Buffer: var buffer }:
            DecodeBuffer(buffer);
            break;
    }
}
```

- `OnConnected` / `OnDisconnected` are private methods — internal to the SM.
- Reconnect decisions happen entirely inside the SM — it either calls `_ops.OnFail()` to give up or buffers requests and emits `ConnectTransport` via `_ops.OnOutbound()`.

### Http3 Pattern (multiplexed)

```csharp
public void DecodeServerData(ITransportInbound data)
{
    switch (data)
    {
        case TransportConnected:
            OnConnected();
            break;
        case TransportDisconnected:
            OnDisconnected();
            break;
        case MultiplexedData { StreamId: var id, Buffer: var buffer }:
            RouteStreamData(id, buffer);
            break;
        case StreamClosed { StreamId: var id }:
            OnStreamClosed(id);
            break;
        case ServerStreamAccepted { StreamId: var id }:
            OnServerStreamAccepted(id);
            break;
        case StreamReadCompleted { StreamId: var id }:
            OnStreamReadCompleted(id);
            break;
        case TransportData:
            _ops.OnWarning("Received untagged TransportData — dropping");
            break;
    }
}

void RouteStreamData(long streamId, ReadOnlyMemory<byte> buffer)
{
    switch (streamId)
    {
        case ControlStreamId:
            DecodeControlFrames(buffer);
            break;
        case QpackEncoderStreamId:
            ProcessQpackEncoderBytes(buffer);
            break;
        case QpackDecoderStreamId:
            ProcessQpackDecoderBytes(buffer);
            break;
        default:
            DecodeRequestStream(streamId, buffer);
            break;
    }
}
```

All stream-ID routing and frame processing that currently lives in `Http30ConnectionStage.HandleTaggedStreamData()` and `ProcessFrameData()` moves into the SM.

## Timer Management

- SM requests timers via `_ops.OnScheduleTimer(name, duration)`.
- Stage mechanically calls `ScheduleOnce(name, duration)`.
- When timer fires, stage calls `_sm.OnTimerFired(name)`.
- SM decides what to do (send PING, check idle timeout, etc.) and signals actions via callbacks.
- SM cancels timers via `_ops.OnCancelTimer(name)`.

Applies to Http20 (keep-alive PING) and Http30 (idle timeout). Http10/Http11 SMs simply never call `OnScheduleTimer`.

## Testing Strategy

### SM Unit Tests (bulk of testing)

- `MockStageOperations : IStageOperations` captures all callbacks into lists.
- Test each SM directly — no Akka TestKit, no stream materialization.
- Fast, deterministic, easy to assert.

```csharp
var ops = new MockStageOperations();
var sm = new Http11StateMachine(ops, settings);

sm.DecodeServerData(new TransportConnected());
sm.OnRequest(request);

Assert.Single(ops.Outbound);  // encoded request emitted
```

### Stage Integration Tests (minimal)

- One test class for `HttpConnectionStageLogic<TSM>` using a `MockStateMachine : IHttpStateMachine`.
- Verifies the mechanical pump: enqueue/dequeue, timer forwarding, PostStop cleanup.
- Covers all protocols at once since they share the same stage.

### What Goes Away

- Per-protocol stage tests that duplicate Pull/Push/queue mechanics.
- Testing frame loops or transport lifecycle decisions in stage tests.
- Protocol behavior tests move entirely to SM unit tests.

## Migration Path

Each protocol can be migrated independently:

1. Define `IHttpStateMachine` and `IStageOperations` interfaces.
2. Create `HttpConnectionStageLogic<TSM>` base.
3. Migrate one protocol at a time (recommend Http11 first as reference):
   - Move all logic from the stage's handler callbacks into the SM's `DecodeServerData` / pattern match.
   - Remove transport lifecycle logic from stage.
   - Move timer logic from stage to SM.
   - Delete the old per-protocol stage logic class.
4. Verify existing tests still pass (behavior unchanged, only location of logic changes).
5. Add `MockStageOperations`-based SM unit tests.
6. Add `MockStateMachine`-based stage pump tests.

## Files Affected

### New Files
- `IHttpStateMachine.cs` — interface
- `IStageOperations.cs` — replace the existing `IStageOperations` interface with the expanded version (adds `OnScheduleTimer`, `OnCancelTimer`, `OnComplete`, `OnFail`; removes `OnReconnectFailed`)
- `HttpConnectionStageLogic.cs` — generic base stage logic

### Modified Files
- `Http10ConnectionStage.cs` — thin outer shell only
- `Http11ConnectionStage.cs` — thin outer shell only
- `Http20ConnectionStage.cs` — thin outer shell only
- `Http30ConnectionStage.cs` — thin outer shell only
- `Http10/StateMachine.cs` — implement `IHttpStateMachine`, absorb stage logic
- `Http11/StateMachine.cs` — implement `IHttpStateMachine`, absorb stage logic
- `Http2/StateMachine.cs` — implement `IHttpStateMachine`, absorb stage logic
- `Http3/StateMachine.cs` — implement `IHttpStateMachine`, absorb stage logic

### Test Files
- New: `MockStageOperations.cs`, `MockStateMachine.cs`
- New: `HttpConnectionStageLogicSpec.cs` (one class for all protocols)
- Modified: existing SM tests updated to use `MockStageOperations` pattern
