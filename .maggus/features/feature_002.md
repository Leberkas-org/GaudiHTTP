<!-- maggus-id: 20260325-122253-feature-027 -->

# Feature 002: Extract ConnectionStage Transport Handlers

## Introduction

The `ConnectionStage` (1032 lines) is a unified Akka.Streams `GraphStage` handling TCP single-stream (HTTP/1.x, HTTP/2) and QUIC multi-stream (HTTP/3) transport. Both code paths are interleaved throughout the class — `_isQuic` branches appear in `HandlePush`, `PreStart` callbacks, `OnUpstreamFinish`, `OnDownstreamFinish`, `PostStop`, and most helper methods. This makes the class hard to read, hard to extend, and fragile when modifying one transport without breaking the other.

This feature extracts transport-specific logic into separate strategy classes behind an `ITransportHandler` interface. ConnectionStage becomes a thin ~200-line orchestrator that:
1. Receives items from the Akka stream
2. On `ConnectItem`, creates the appropriate `ITransportHandler` (TCP or QUIC based on `TcpOptions` vs `QuicOptions`)
3. Delegates all item handling, connection lifecycle, and cleanup to the active handler

### Architecture Context

- **Components involved:** `Transport/ConnectionStage.cs`, new `Transport/ITransportHandler.cs`, `Transport/TcpTransportHandler.cs`, `Transport/QuicTransportHandler.cs`, `Transport/IStageCallbacks.cs`
- **Existing patterns respected:** All stages use `GraphStageLogic` with `GetAsyncCallback<T>` for thread-safe bridging. Handlers will receive this factory to register their own callbacks.
- **No public API changes:** `ConnectionStage` remains the sole `GraphStage`; handlers are `internal` implementation details.

## Goals

- Reduce `ConnectionStage` to ~200 lines — a thin orchestrator with no transport-specific logic
- Encapsulate TCP connection lifecycle (pool acquire/release, single handle, lease management) in `TcpTransportHandler`
- Encapsulate QUIC multi-stream lifecycle (QuicConnectionManager, request/control/encoder handles, typed stream routing) in `QuicTransportHandler`
- Eliminate all `_isQuic` branches from ConnectionStage
- Maintain identical runtime behaviour — zero functional changes, all existing tests must pass unchanged
- Enable future transport additions (e.g. Unix domain sockets) by implementing `ITransportHandler`

## Tasks

### TASK-027-001: Define ITransportHandler and IStageCallbacks interfaces
**Description:** As a developer, I want clean interfaces that define the contract between ConnectionStage and transport handlers, so that each handler can be implemented and tested independently.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-027-002, TASK-027-003
**Parallel:** no — other tasks depend on these interfaces
**Model:** opus — critical API design decision

**Acceptance Criteria:**
- [ ] `IStageCallbacks` interface created in `Transport/IStageCallbacks.cs` with methods that handlers need from the stage:
  - `void PushOutput(IInputItem item)` — push or enqueue to pending reads
  - `void SignalPullInput()` — TryPull on inlet
  - `bool IsOutputAvailable()` — check outlet availability
  - `bool IsInputClosed()` — check inlet closed
  - `bool HasInputBeenPulled()` — check inlet pulled
  - `void ScheduleConnectTimeout(TimeSpan timeout)` — schedule timer
  - `void CancelConnectTimeout()` — cancel timer
  - `void RequestCompleteStage()` — request stage completion
  - `void LogWarning(string format, params object[] args)` — logging
  - `Action<T> GetAsyncCallback<T>(Action<T> handler)` — async callback factory
  - `Action GetAsyncCallback(Action handler)` — parameterless overload
- [ ] `ITransportHandler` interface created in `Transport/ITransportHandler.cs` with:
  - `void Initialize(IStageCallbacks callbacks)` — called during PreStart, handler registers its own async callbacks here
  - `void HandleConnectItem(ConnectItem connect)` — initiate connection acquisition
  - `void HandleDataItem(DataItem dataItem)` — write data to transport
  - `void HandleTaggedItem(Http3TaggedItem tagged)` — route tagged QUIC items (QUIC handler implements; TCP handler throws or ignores)
  - `void HandleConnectionReuseItem(ConnectionReuseItem reuseItem)` — connection reuse decision
  - `void HandleMaxConcurrentStreamsItem(MaxConcurrentStreamsItem item)` — update stream capacity
  - `void HandleStreamAcquireItem(StreamAcquireItem item)` — reserve stream capacity
  - `void OnUpstreamFinished()` — upstream completed
  - `void OnConnectTimeout()` — connection acquisition timed out
  - `void Cleanup()` — dispose resources (PostStop)
- [ ] Both interfaces are `internal`
- [ ] Interfaces reside in `TurboHttp.Transport` namespace
- [ ] Build succeeds with zero errors

### TASK-027-002: Implement TcpTransportHandler
**Description:** As a developer, I want all TCP single-stream logic (HTTP/1.x, HTTP/2) encapsulated in one class, so that TCP connection lifecycle, lease management, and inbound pump logic are isolated from QUIC code.

**Token Estimate:** ~60k tokens
**Predecessors:** TASK-027-001
**Successors:** TASK-027-004
**Parallel:** yes — can run alongside TASK-027-003
**Model:** opus — complex state machine with async callbacks

**Acceptance Criteria:**
- [ ] `TcpTransportHandler` created in `Transport/TcpTransportHandler.cs`
- [ ] Constructor receives `ConnectionPool pool`
- [ ] `Initialize(IStageCallbacks)` registers these async callbacks via `callbacks.GetAsyncCallback<T>`:
  - `_onLeaseAcquired` — handles ConnectionLease receipt, increments `_connectionGen`, starts inbound pump, flushes pending writes
  - `_onInboundData` — pushes DataItem to stage via `callbacks.PushOutput`
  - `_onOutboundWriteDone` — signals `callbacks.SignalPullInput`
  - `_onOutboundWriteFailed` — marks lease no-reuse, returns to pool, emits CloseSignalItem, clears handle
  - `_onAcquisitionFailed` — emits CloseSignalItem, signals pull
  - `_onInboundComplete` — generation-guarded close handling, emits CloseSignalItem, returns lease
  - `_onFlushNext` — continues flushing pending writes
- [ ] All TCP state moved from ConnectionStage.Logic:
  - `_handle`, `_currentLease`, `_leaseReturned`, `_connectionGen`, `_currentKey`
  - `_pendingConnect`, `_pendingWrites`, `_upstreamFinished`
  - `_pumpCts`
- [ ] Methods extracted: `AcquireConnection`, `ReturnLeaseToPool`, `StartInboundPump`, `StopInboundPump`, `FlushPendingWrites`, `FlushNext`
- [ ] `HandleDataItem` writes to `_handle.OutboundWriter` with continuations, or buffers to `_pendingWrites` if handle not yet available
- [ ] `HandleConnectionReuseItem` calls `ReturnLeaseToPool` with reuse decision
- [ ] `HandleMaxConcurrentStreamsItem` calls `lease.UpdateMaxConcurrentStreams`
- [ ] `HandleStreamAcquireItem` calls `lease.MarkBusy`
- [ ] `HandleTaggedItem` is a no-op (TCP does not use tagged items)
- [ ] `OnUpstreamFinished` sets `_upstreamFinished = true`, completes stage if no handle
- [ ] `OnConnectTimeout` emits CloseSignalItem for pending connect, signals pull
- [ ] `Cleanup` stops inbound pump, disposes current lease
- [ ] Class is `internal sealed`
- [ ] Build succeeds with zero errors

### TASK-027-003: Implement QuicTransportHandler
**Description:** As a developer, I want all QUIC multi-stream logic (HTTP/3) encapsulated in one class, so that QuicConnectionManager lifecycle, multi-handle management, typed stream routing, and multiple inbound pumps are isolated from TCP code.

**Token Estimate:** ~70k tokens
**Predecessors:** TASK-027-001
**Successors:** TASK-027-004
**Parallel:** yes — can run alongside TASK-027-002
**Model:** opus — complex multi-stream state management

**Acceptance Criteria:**
- [ ] `QuicTransportHandler` created in `Transport/QuicTransportHandler.cs`
- [ ] Constructor is parameterless (QuicConnectionManager is created per-connect)
- [ ] `Initialize(IStageCallbacks)` registers these async callbacks:
  - `_onRequestLeaseAcquired` — stores request handle, starts request inbound pump, opens control + encoder streams, starts inbound accept loop
  - `_onTypedLeaseAcquired` — stores control/encoder handle, flushes pending items
  - `_onInboundData` — pushes items (plain or Http3InputTaggedItem) via `callbacks.PushOutput`
  - `_onOutboundWriteDone` — signals `callbacks.SignalPullInput`
  - `_onOutboundWriteFailed` — emits CloseSignalItem, clears all handles
  - `_onAcquisitionFailed` — emits CloseSignalItem for pending connect
  - `_onInboundComplete` — emits CloseSignalItem, clears all handles
  - `_onInboundStreamReady` — starts inbound pump for server-initiated stream
- [ ] All QUIC state moved from ConnectionStage.Logic:
  - `_quicManager`, `_requestHandle`, `_controlHandle`, `_encoderHandle`
  - `_pendingControlItems`, `_pendingEncoderItems`, `_activeLeases`, `_quicPumpCancellations`
  - `_pendingTypedStreamType`, `_currentKey`, `_pendingConnect`
- [ ] Methods extracted: `AcquireQuicConnection`, `OpenTypedStream`, `StartQuicInboundPump`, `StopAllQuicPumps`, `HandleTaggedItem`, `WriteToHandle`, `FlushPendingQuicItems`
- [ ] `HandleDataItem` routes untagged DataItem to request handle
- [ ] `HandleConnectionReuseItem` is a no-op (QUIC lifecycle is managed by QuicConnectionManager)
- [ ] `HandleMaxConcurrentStreamsItem` is a no-op (QUIC transport handles this)
- [ ] `HandleStreamAcquireItem` is a no-op (QUIC stream acquisition via QuicConnectionManager)
- [ ] `OnUpstreamFinished` stops all pumps and requests stage completion
- [ ] `OnConnectTimeout` emits CloseSignalItem for pending connect
- [ ] `Cleanup` stops all pumps, disposes all leases, disposes QuicConnectionManager
- [ ] Class is `internal sealed`
- [ ] Build succeeds with zero errors

### TASK-027-004: Rewrite ConnectionStage as thin orchestrator
**Description:** As a developer, I want ConnectionStage to be a ~200-line orchestrator that delegates all transport logic to the active `ITransportHandler`, so that the stage is easy to read and extend.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-027-002, TASK-027-003
**Successors:** TASK-027-005
**Parallel:** no — depends on both handlers being complete

**Acceptance Criteria:**
- [ ] ConnectionStage.Logic implements `IStageCallbacks` directly (it already has access to `Push`, `Pull`, `IsAvailable`, etc.)
- [ ] `_transport` field of type `ITransportHandler?` — null until first `ConnectItem`
- [ ] PreStart: no transport-specific callbacks — only pull inlet
- [ ] `HandlePush` dispatch:
  - `ConnectItem`: create `ITransportHandler` based on `connect.Options is QuicOptions` → `new QuicTransportHandler()` or `new TcpTransportHandler(pool)`, call `_transport.Initialize(this)`, then `_transport.HandleConnectItem(connect)`
  - `Http3TaggedItem`: `_transport.HandleTaggedItem(tagged)`
  - `DataItem`: `_transport.HandleDataItem(dataItem)`
  - `ConnectionReuseItem`: `_transport.HandleConnectionReuseItem(reuseItem)`
  - `MaxConcurrentStreamsItem`: `_transport.HandleMaxConcurrentStreamsItem(item)`
  - `StreamAcquireItem`: `_transport.HandleStreamAcquireItem(item)`
- [ ] `OnUpstreamFinish`: `_transport?.OnUpstreamFinished()` or `CompleteStage()` if no transport
- [ ] `OnDownstreamFinish`: `_transport?.Cleanup()`, `CompleteStage()`
- [ ] `OnTimer(ConnectTimerKey)`: `_transport?.OnConnectTimeout()`
- [ ] `PostStop`: `_transport?.Cleanup()`
- [ ] `IStageCallbacks` implementation maps to stage primitives:
  - `PushOutput` → push or enqueue to `_pendingReads`
  - `SignalPullInput` → TryPull
  - `IsOutputAvailable` → `IsAvailable(_stage._out)`
  - etc.
- [ ] **Zero `_isQuic` branches remain** in ConnectionStage
- [ ] **No transport-specific state** remains in Logic (only `_transport`, `_pendingReads`)
- [ ] ConnectionStage is **under 250 lines** total (outer class + Logic)
- [ ] Build succeeds with zero errors
- [ ] `#pragma warning disable CA1416` moves to QuicTransportHandler (only file that needs it)

### TASK-027-005: Verify all tests pass and no regressions
**Description:** As a developer, I want to verify that the refactoring is purely structural with zero functional changes, by running the full test suite and checking build diagnostics.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-027-004
**Successors:** TASK-027-006
**Parallel:** no — must run after rewrite is complete

**Acceptance Criteria:**
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — zero errors, zero warnings (except pre-existing)
- [ ] `dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj` — all tests pass
- [ ] `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` — all tests pass
- [ ] `dotnet test src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj` — all tests pass (if applicable)
- [ ] No new compiler warnings introduced
- [ ] No behavioural differences — this is a pure refactoring

### TASK-027-006: Verify line counts and code quality
**Description:** As a developer, I want to confirm the refactoring achieved its goals: ConnectionStage is ~200 lines, handlers are well-structured, and port naming conventions are respected.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-027-005
**Successors:** none
**Parallel:** no — final validation

**Acceptance Criteria:**
- [ ] `ConnectionStage.cs` is under 250 lines
- [ ] `TcpTransportHandler.cs` is under 400 lines
- [ ] `QuicTransportHandler.cs` is under 450 lines
- [ ] `ITransportHandler.cs` is under 40 lines
- [ ] `IStageCallbacks.cs` is under 30 lines
- [ ] Port names unchanged: `"Connection.In"`, `"Connection.Out"`
- [ ] Stage port validator passes: no naming convention violations
- [ ] No dead code (unused fields, unreachable branches)
- [ ] All new classes are `internal sealed`

## Task Dependency Graph

```
TASK-027-001 ──→ TASK-027-002 ──→ TASK-027-004 ──→ TASK-027-005 ──→ TASK-027-006
             └──→ TASK-027-003 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-027-001 | ~30k | none | no | opus |
| TASK-027-002 | ~60k | 001 | yes (with 003) | opus |
| TASK-027-003 | ~70k | 001 | yes (with 002) | opus |
| TASK-027-004 | ~50k | 002, 003 | no | — |
| TASK-027-005 | ~25k | 004 | no | — |
| TASK-027-006 | ~15k | 005 | no | — |

**Total estimated tokens:** ~250k

## Functional Requirements

- FR-1: `ITransportHandler` must define a complete contract for transport lifecycle (connect, write, reuse, cleanup) — no transport logic leaks into ConnectionStage
- FR-2: `IStageCallbacks` must expose all stage primitives that handlers need — handlers must never directly access `GraphStageLogic` methods
- FR-3: `TcpTransportHandler` must replicate the exact TCP connection lifecycle: pool acquire with timeout, lease management with generation counter, single inbound pump, pending write buffering, idempotent lease return
- FR-4: `QuicTransportHandler` must replicate the exact QUIC lifecycle: QuicConnectionManager creation per ConnectItem, request/control/encoder stream opening, multi-pump management, typed stream routing via `Http3TaggedItem`, server-initiated stream acceptance
- FR-5: ConnectionStage must create the appropriate handler based on `ConnectItem.Options` type (`QuicOptions` → QUIC, otherwise → TCP)
- FR-6: Handler creation must be lazy — no handler exists until the first `ConnectItem` arrives
- FR-7: All async callbacks must be registered via `IStageCallbacks.GetAsyncCallback<T>` during `Initialize()` to ensure thread-safe bridging into the stage event loop
- FR-8: The `_pendingReads` queue must remain in ConnectionStage (shared between both transport paths, used by outlet handler)

## Non-Goals

- No functional changes to connection behaviour — this is a pure structural refactoring
- No new transport implementations (Unix domain sockets, named pipes, etc.) — only TCP and QUIC
- No changes to `ConnectionPool`, `QuicConnectionManager`, `ConnectionLease`, or `ConnectionHandle`
- No changes to message types (`IOutputItem`, `IInputItem`, `ConnectItem`, etc.)
- No changes to port names or stage shape
- No test modifications — all existing tests must pass as-is
- No performance optimisation — maintain identical allocation patterns and code paths

## Technical Considerations

- **Thread safety:** Handlers are only ever called from the stage's event loop (via Akka dispatcher). No additional synchronization needed within handlers. Async callbacks registered via `GetAsyncCallback<T>` ensure safe re-entry.
- **Callback lifetime:** Callbacks are registered once during `Initialize()` and remain valid for the handler's lifetime. They capture the `IStageCallbacks` reference which is stable (the Logic instance itself).
- **Handler replacement:** If a new `ConnectItem` arrives with a different transport type (e.g., TCP then QUIC), the old handler's `Cleanup()` must be called before creating the new one. This is an edge case that likely never occurs in practice (protocol is fixed per pipeline) but must be handled defensively.
- **`CA1416` platform guard:** The `#pragma warning disable CA1416` currently on ConnectionStage should move to `QuicTransportHandler.cs` only, since it's the only file referencing QUIC APIs.
- **`_pendingReads` stays in Logic:** The outlet `onPull` handler dequeues from this queue. It cannot move to a transport handler because it's transport-agnostic.

## Success Metrics

- ConnectionStage.cs under 250 lines (from 1032 — **>75% reduction**)
- Zero `_isQuic` branches in ConnectionStage
- Zero test failures across all test projects
- Zero new compiler warnings
- Clear separation: reading `TcpTransportHandler` requires zero QUIC knowledge and vice versa

## Open Questions

*None — all design decisions resolved.*
