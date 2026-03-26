<!-- maggus-id: 20260326-120000-feature-028 -->

# Feature 028: Evergreen Pipeline with KillSwitch-Shutdown

## Introduction

Replace the broken polling-based completion deferral in `GroupByHostKeyStage` with an evergreen pipeline architecture where the Akka.Streams graph stays alive for the entire client lifetime and terminates exclusively via a `SharedKillSwitch` on `Dispose()`.

This eliminates the HTTP/1.0 re-injection deadlock (Feature 027 root cause) by removing the timing window between `ChannelSource` completion and feature-stage re-injection entirely.

### Architecture Context

- **Components involved**: `ClientStreamInstanceActor`, `ClientStreamOwnerActor`, `GroupByHostKeyStage`, `PendingWorkTracker`, `TurboClientStreamManager`, `Engine`, feature BidiStages
- **Root cause**: `GroupByHostKeyStage.TryFinish()` uses polling with `Task.Delay` (5 retries x 10ms = 50ms max) to wait for pending work, then force-completes — too short for real HTTP round-trips
- **Conceptual flaws fixed**:
  1. Polling hack in `TryFinish()` replaced by KillSwitch-coordinated shutdown
  2. Double bookkeeping (`Owner._pending` + `PendingWorkTracker._count`) eliminated — single source of truth
  3. `ContentEncodingBidiStage` receives tracker but never uses it — removed
- **Breaking changes**: None — internal refactor only
- **Net result**: ~100 fewer lines of code, cleaner architecture, zero deadlock race window

### Analysis Summary (Sections 1-6)

**What was implemented (Feature 001 / poc2):**
- TASK-001-001 to 009: Actor protocol, Owner/Instance actors, PendingWork tracking, integration, tests, public API — all DONE
- TASK-001-010/011: Stress tests and docs — OPEN

**Feature 027 (Deadlock Diagnosis):**
- Root cause confirmed: `GroupByHostKeyStage` completes substreams prematurely when `ChannelSource` signals completion while feature BidiStages still have re-injections in flight
- Hypothesis H1 (Substream Death) CONFIRMED; H2-H4 rejected
- 10x batch validation (TASK-027-004) never executed

**Conceptual Weaknesses Identified:**
1. **Polling instead of backpressure** (CRITICAL) — 50ms max wait, then force-complete destroys what PendingWork signals protect
2. **Double bookkeeping** — `Owner._pending` and `PendingWorkTracker._count` can diverge
3. **Actor overhead for a stream problem** — The actual fix site (`TryFinish`) is ~30 lines; the actor infrastructure is 400+ lines
4. **PendingWorkTracker sends actor messages from stage callbacks** — couples stream layer to actor layer
5. **Feature 002 irrelevant** — HTTP/2 priorities deprecated in RFC 9113 Section 5.3.2
6. **ContentEncodingBidiStage bug** — receives tracker parameter but never calls Increment/Decrement

## Goals

- **Eliminate the deadlock race** — Pipeline only terminates via explicit `KillSwitch.Shutdown()`, never via `ChannelSource` completion propagation
- **Single source of truth** — Remove `Owner._pending`, use only `PendingWorkTracker` as the authoritative counter
- **Simplify GroupByHostKeyStage** — Remove polling/timer code, tracker dependency; TryFinish becomes trivial queue-drain
- **Add OnDrained callback** — PendingWorkTracker fires a callback when count transitions to zero, enabling coordinated shutdown
- **Clean up dead code** — Remove unused tracker from ContentEncodingBidiStage
- **Validate** — 10x batch run integration tests with zero deadlocks

## Concept: How the Evergreen Pipeline Works

### Core Principle

**The stream lives as long as the client.** No premature completion, no polling, no race.

```
Current (broken):
  ChannelSource completes -> Completion propagates -> GroupByHostKey TryFinish()
  -> RACE with Feature-Stage Re-Injection -> Deadlock

New (clean):
  ChannelSource delivers Requests -> Pipeline processes -> waits for next Request
  -> Dispose() -> KillSwitch.Shutdown() -> clean terminate
```

The pipeline terminates EXCLUSIVELY through a `SharedKillSwitch` controlled by the Owner actor. `ChannelSource` completion is NOT used as a shutdown signal. This eliminates the timing window between completion and re-injection — the race no longer exists.

### Architecture Overview

```
                    +-------------------------------------------------------------+
                    |              Evergreen Pipeline                              |
                    |                                                              |
  Channel ------>  ChannelSource --> [KillSwitch] --> RequestEnricher              |
                    |                     ^              |                         |
                    |                     |              v                         |
                    |               Owner Actor    BidiStack (Feature-Stages)      |
                    |               holds KillSwitch     |                         |
                    |                     |              v                         |
                    |                     |        ProtocolCore                    |
                    |                     |         (GroupByHostKey                |
                    |                     |          -> Connection                 |
                    |                     |          -> MergeSubstreams)           |
                    |                     |              |                         |
                    |                     |              v                         |
  Channel <------  ResponseWriter <---- Sink.ForEach                              |
                    |                                                              |
                    +-------------------------------------------------------------+

  Dispose() --> Owner.Shutdown --> wait for PendingWork==0 --> KillSwitch.Shutdown()
                                                                      |
                                                        Pipeline terminates cleanly
```

### Lifecycle: Normal Operation

```
1. User writes Request to Channel
2. ChannelSource reads Request, pushes into Pipeline
3. Request flows through BidiStack -> ProtocolCore -> TCP -> Response
4. Feature-Stage decides: Retry/Redirect needed?
   YES -> IncrementPending(), push new Request -> Pipeline processes it
        -> Response arrives -> DecrementPending(), push Response downstream
   NO  -> push Response directly downstream
5. Sink.ForEach writes Response to ResponseWriter Channel
6. Pipeline waits for next Request (idle but ALIVE)
```

No completion signal. The pipeline is idle but active — just like a connection pool.

### Lifecycle: Shutdown (Dispose)

```
1. TurboClientStreamManager.Dispose() / DisposeAsync()
2. Requests.TryComplete()              -> prevents new Requests into Channel
   (ChannelSource completion is ABSORBED by KillSwitch — does NOT propagate)
3. Owner.Tell(Shutdown)
4. Owner checks: tracker.IsPending?
   YES -> register OnDrained callback, start 5s safety timeout
   NO  -> proceed immediately to step 5
5. Owner calls killSwitch.Shutdown()   -> clean pipeline termination
   - KillSwitch cancels upstream (ChannelSource stops)
   - KillSwitch completes downstream (Pipeline drains)
   - GroupByHostKeyStage.onUpstreamFinish -> TryFinish() (trivial, no PendingWork check)
   - All substreams complete -> MergeSubstreams completes -> Sink completes
6. Instance actor receives StreamSinkCompleted -> PostStop -> Cleanup
7. Owner receives Terminated -> Self-Stop
8. ResponseWriter.TryComplete()        -> downstream consumers terminate
```

### Why This Works — Proof by Elimination

The deadlock race was:

```
Current:
  ChannelSource.Complete -> propagates -> GroupByHostKey.onUpstreamFinish -> TryFinish()
  SIMULTANEOUSLY: Feature-Stage wants to re-inject -> IsPending not yet set -> DEADLOCK
```

With KillSwitch:

```
New:
  ChannelSource.Complete -> KillSwitch absorbs completion -> NOTHING HAPPENS
  Feature-Stage re-injects -> Pipeline is alive -> Request goes through -> Response arrives
  Later: Dispose() -> Owner waits for IsPending==false -> KillSwitch.Shutdown()
  -> Pipeline drains cleanly -> no race possible because PendingWork is already 0
```

**The race no longer exists** because completion only fires AFTER all re-injections are done.

### Edge Cases

| Case | Behaviour |
|------|-----------|
| Feature-Stage re-injects while shutdown waits | Owner waits — OnDrained fires only when counter==0 |
| PendingWork counter has bug, never reaches 0 | Safety timeout after 5s -> Force KillSwitch.Shutdown() + Warning |
| Instance crashes while pipeline is running | Owner detects Terminated -> tracker.Reset() -> spawns new Instance |
| Dispose() with no in-flight requests | tracker.IsPending==false -> immediately KillSwitch -> immediately done |
| Concurrent Feature-Stage Increment/Decrement | Interlocked operations are thread-safe (as before) |
| KillSwitch.Shutdown() and ChannelSource.Complete simultaneously | KillSwitch absorbs both -> no problem |

## Tasks

### TASK-028-001: Remove Unused Tracker from ContentEncodingBidiStage

**Description:** Remove the `IPendingWorkTracker` parameter and field from `ContentEncodingBidiStage` — it receives the tracker but never calls `IncrementPending()`/`DecrementPending()`. Also update `Engine.cs` to stop passing the tracker.

**Token Estimate:** ~5k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — independent cleanup

**Acceptance Criteria:**
- [ ] Remove `IPendingWorkTracker` constructor parameter from `ContentEncodingBidiStage`
- [ ] Remove `_pendingWorkTracker` field from `ContentEncodingBidiStage`
- [ ] Update `Engine.cs` to not pass tracker to `ContentEncodingBidiStage`
- [ ] `dotnet build` succeeds with zero errors
- [ ] All existing tests pass

**Files:**
- `src/TurboHttp/Streams/Stages/Features/ContentEncodingBidiStage.cs`
- `src/TurboHttp/Streams/Engine.cs`

---

### TASK-028-002: Add OnDrained Callback to IPendingWorkTracker

**Description:** Extend `IPendingWorkTracker` with a `void OnDrained(Action callback)` method that fires when the pending count transitions from >0 to 0. This replaces the polling approach with an event-driven coordination mechanism for shutdown.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-028-004
**Parallel:** yes — can run alongside TASK-028-001

**Acceptance Criteria:**
- [ ] Add `void OnDrained(Action callback)` to `IPendingWorkTracker` interface
- [ ] Implement in `PendingWorkTracker`: store callback, fire on count transition to 0
- [ ] Handle edge case: if counter is already 0 when OnDrained is called, fire immediately
- [ ] Fire callback in `DecrementPending()` when `newValue == 0`
- [ ] Fire callback in `Reset()` when `previous > 0`
- [ ] Use `Interlocked.Exchange` for one-shot callback (fire once, then clear)
- [ ] Unit tests: callback fires on >0 to 0 transition
- [ ] Unit tests: callback fires immediately if already at 0
- [ ] Unit tests: callback is one-shot (does not fire again on subsequent transitions)
- [ ] `dotnet build` + `dotnet test` green

**Files:**
- `src/TurboHttp/Client/ActorProtocol.cs` (interface)
- `src/TurboHttp/Client/PendingWorkTracker.cs` (implementation)
- `src/TurboHttp.Tests/` (new test file for OnDrained)

**Code Design:**

```csharp
// IPendingWorkTracker addition:
void OnDrained(Action callback);

// PendingWorkTracker implementation:
private Action? _onDrained;

public void OnDrained(Action callback)
{
    Volatile.Write(ref _onDrained, callback);
    if (!IsPending)
    {
        var cb = Interlocked.Exchange(ref _onDrained, null);
        cb?.Invoke();
    }
}

public void DecrementPending()
{
    var newValue = Interlocked.Decrement(ref _count);
    if (newValue < 0) { /* self-heal */ return; }
    _logger?.Debug("PendingWorkTracker: decremented to {0}", newValue);

    if (newValue == 0)
    {
        var cb = Interlocked.Exchange(ref _onDrained, null);
        cb?.Invoke();
    }
}
```

---

### TASK-028-003: Add SharedKillSwitch to Pipeline Materialization

**Description:** Insert an `Akka.Streams.KillSwitches.Shared` flow between `ChannelSource` and the engine flow in `ClientStreamInstanceActor`. The KillSwitch absorbs `ChannelSource` completion (preventing premature pipeline shutdown) and provides the explicit shutdown mechanism.

**Token Estimate:** ~20k tokens
**Predecessors:** none
**Successors:** TASK-028-004
**Parallel:** yes — can run alongside TASK-028-001 and TASK-028-002

**Acceptance Criteria:**
- [ ] Add `private SharedKillSwitch? _killSwitch` field to `ClientStreamInstanceActor`
- [ ] Create `KillSwitches.Shared(...)` in `HandleInitializeStream` before materialization
- [ ] Insert `.Via(_killSwitch.Flow<HttpRequestMessage>())` between `ChannelSource.FromReader()` and `.Via(engineFlow)`
- [ ] Modify `HandleRequestShutdown()`: fire `_killSwitch?.Shutdown()` instead of `Context.Stop(Self)`
- [ ] Modify `HandleStreamSinkCompleted()`: on success, call `Context.Stop(Self)` (after pipeline drain)
- [ ] `PostStop` still disposes materializer and pool (cleanup for error cases)
- [ ] `dotnet build` succeeds
- [ ] Existing tests pass (pipeline now stays alive longer — tests may need timeout adjustments)

**Files:**
- `src/TurboHttp/Client/ClientStreamInstance.cs`

**Code Design:**

```csharp
private SharedKillSwitch? _killSwitch;

private void HandleInitializeStream(InstanceMsg.InitializeStream init)
{
    // ... pool, engine as before ...

    _killSwitch = KillSwitches.Shared($"client-{Self.Path.Name}");

    var completionTask = ChannelSource.FromReader(init.RequestReader)
        .Via(_killSwitch.Flow<HttpRequestMessage>())   // NEW
        .Via(engineFlow)
        .RunWith(
            Sink.ForEach<HttpResponseMessage>(msg => init.ResponseWriter.TryWrite(msg)),
            _materializer);

    MonitorSinkCompletion(completionTask);
    // ...
}

private void HandleRequestShutdown()
{
    _log.Debug("Shutdown requested, firing KillSwitch");
    _killSwitch?.Shutdown();
    // Pipeline drains -> StreamSinkCompleted arrives -> then Context.Stop(Self)
}

private void HandleStreamSinkCompleted(StreamSinkCompleted completed)
{
    if (completed.Error is not null)
    {
        Context.Parent.Tell(new InstanceMsg.StreamFailed(completed.Error));
    }
    else
    {
        _log.Debug("Pipeline drained, stopping instance");
        Context.Stop(Self);
    }
}
```

---

### TASK-028-004: Simplify Owner — Remove Double Bookkeeping, Add OnDrained Shutdown

**Description:** Remove the redundant `_pending` counter from `ClientStreamOwnerActor`. Shutdown decisions use `tracker.IsPending` directly (single source of truth). Shutdown coordination uses the new `OnDrained` callback instead of relying on `PendingWorkSignal` messages.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-028-002, TASK-028-003
**Successors:** TASK-028-005
**Parallel:** no — depends on OnDrained and KillSwitch being in place

**Acceptance Criteria:**
- [ ] Remove `_pending` field from `ClientStreamOwnerActor`
- [ ] `HandlePendingWorkSignal()` — logging only, no counter update
- [ ] `HandleShutdown()` — use `tracker.IsPending` directly; if pending, register `OnDrained` callback via `GetAsyncCallback`
- [ ] OnDrained callback triggers `RequestInstanceShutdown()` (which sends `RequestShutdown` to instance)
- [ ] Safety timeout (5s) still fires `RequestInstanceShutdown()` as force-shutdown
- [ ] Remove deferred idle request logic tied to `_pending` (replaced by KillSwitch)
- [ ] `dotnet build` + `dotnet test` green
- [ ] Existing Owner lifecycle tests updated/pass

**Files:**
- `src/TurboHttp/Client/ClientStreamOwner.cs`

**Code Design:**

```csharp
private void HandleShutdown()
{
    if (_shuttingDown) return;
    _shuttingDown = true;

    var tracker = _createRequest?.Pipeline.PendingWorkTracker;

    if (tracker is null || !tracker.IsPending)
    {
        RequestInstanceShutdown();
    }
    else
    {
        _log.Info("Waiting for pending work to drain before shutdown");
        var drainedCallback = GetAsyncCallback<NotUsed>(_ => RequestInstanceShutdown());
        tracker.OnDrained(() => drainedCallback(NotUsed.Instance));
        Timers.StartSingleTimer(ShutdownTimerKey, ShutdownTimeoutExpired.Instance, ShutdownTimeout);
    }
}
```

---

### TASK-028-005: Simplify GroupByHostKeyStage — Remove Polling and Tracker Dependency

**Description:** Remove all polling/timer code from `GroupByHostKeyStage.TryFinish()`. The stage no longer needs to check `IPendingWorkTracker` because the KillSwitch guarantees that completion only arrives after all re-injections are done. `TryFinish` becomes a trivial queue-drain check.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-028-004
**Successors:** TASK-028-006
**Parallel:** no — must be done after KillSwitch is in place

**Acceptance Criteria:**
- [ ] Remove `IPendingWorkTracker` constructor parameter from `GroupByHostKeyStage`
- [ ] Remove `_pendingWorkRetryCount`, `_onPendingWorkRetry`, `MaxPendingWorkRetries`, `InitialRetryDelay` fields
- [ ] Remove `Task.Delay().ContinueWith()` polling logic from `TryFinish()`
- [ ] Simplify `TryFinish()` to only check subflow queue drain (pending items + offering state)
- [ ] Update `ProtocolCoreGraphBuilder` / `Engine` to not pass tracker to `GroupByHostKeyStage`
- [ ] `dotnet build` + `dotnet test` green
- [ ] Stage is ~40 lines shorter

**Files:**
- `src/TurboHttp/Streams/Stages/Routing/GroupByHostKeyStage.cs`
- `src/TurboHttp/Streams/ProtocolCoreGraphBuilder.cs` (or wherever tracker is passed to the stage)
- `src/TurboHttp/Streams/Engine.cs`

**Code Design:**

```csharp
private void TryFinish()
{
    if (_subflows.Values.Any(state => !state.IsDead && (state.Pending.Count > 0 || state.Offering)))
    {
        Log.Debug("GroupByHostKeyStage: TryFinish deferred - subflows still draining");
        return;
    }

    foreach (var state in _subflows.Values)
        if (!state.IsDead) state.Queue.Complete();

    Log.Debug("GroupByHostKeyStage: completing stage, {0} substreams", _subflows.Count);
    CompleteStage();
}
```

---

### TASK-028-006: Validation — 10x Batch Run and New Tests

**Description:** Run comprehensive validation: all existing tests green, 10x batch integration test runs with zero deadlocks, and new tests covering the evergreen pipeline behaviour.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-028-005
**Successors:** none
**Parallel:** no — final validation gate

**Acceptance Criteria:**
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests green
- [ ] `dotnet test ./src/TurboHttp.IntegrationTests/` — H10 reinjection tests pass
- [ ] 10x consecutive batch runs of integration tests — zero deadlocks
- [ ] New test: "Pipeline stays alive after request completes" — send Request, get Response, send another Request
- [ ] New test: "Dispose waits for pending re-injection" — trigger Retry, Dispose while Retry runs, Response still arrives
- [ ] New test: "PendingWorkTracker.OnDrained fires on transition to zero"
- [ ] New test: "KillSwitch shutdown drains pipeline cleanly" — in-flight Request is still processed
- [ ] Log verification: no "force-completing after pending-work retries", no `Task.Delay` in GroupByHostKeyStage

**Files:**
- `src/TurboHttp.Tests/` (new PendingWorkTracker tests)
- `src/TurboHttp.StreamTests/` (new KillSwitch/evergreen pipeline tests)
- `src/TurboHttp.IntegrationTests/` (new lifecycle tests)

## Affected Files (Complete)

| File | Change | Scope |
|------|--------|-------|
| `Client/ActorProtocol.cs` | Add `IPendingWorkTracker.OnDrained()` | +3 lines |
| `Client/PendingWorkTracker.cs` | `OnDrained()` impl, callback in `DecrementPending`/`Reset` | +20 lines |
| `Client/ClientStreamInstance.cs` | KillSwitch field, pipeline with KillSwitch, shutdown via KillSwitch | ~30 lines changed |
| `Client/ClientStreamOwner.cs` | Remove `_pending`, OnDrained callback for shutdown, simplify | ~80 lines removed |
| `Streams/Stages/Routing/GroupByHostKeyStage.cs` | Simplify TryFinish(), remove tracker dependency | ~40 lines removed |
| `Streams/Stages/Features/ContentEncodingBidiStage.cs` | Remove tracker field/parameter | ~5 lines removed |
| `Streams/Engine.cs` | Don't pass tracker to ContentEncoding | 1 line |
| `TurboClientStreamManager.cs` | Minimal change to Dispose comments | ~5 lines |

**Net: ~100 fewer lines of code**, cleaner architecture, no deadlock.

## Implementation Order

```
Phase 1: Quick Fixes (TASK-028-001)
  -> Remove ContentEncoding tracker
  -> Verify build

Phase 2: PendingWorkTracker.OnDrained (TASK-028-002)
  -> Extend interface
  -> Implementation
  -> Unit tests for OnDrained (counter transition >0 -> 0, edge case counter==0)

Phase 3: KillSwitch Integration (TASK-028-003)
  -> ClientStreamInstanceActor: SharedKillSwitch in pipeline
  -> Shutdown flow: KillSwitch instead of Context.Stop
  -> StreamSinkCompleted -> Context.Stop (after drain)

Phase 4: Owner Simplification (TASK-028-004)
  -> Remove _pending counter
  -> Shutdown via OnDrained + KillSwitch coordination
  -> PendingWorkSignal for logging only

Phase 5: GroupByHostKeyStage Simplification (TASK-028-005)
  -> Remove polling/timer code
  -> TryFinish() trivial: only queue-drain, no tracker check
  -> Remove tracker dependency from constructor

Phase 6: Validation (TASK-028-006)
  -> All existing tests green
  -> 10x batch run integration tests — zero deadlocks
  -> New tests: evergreen pipeline, OnDrained, KillSwitch drain
```

## Verification

### Automated Tests:
1. `dotnet test ./src/TurboHttp.sln` — all tests green
2. `dotnet test ./src/TurboHttp.IntegrationTests/` — H10 reinjection tests
3. 10x batch run integration tests — zero deadlocks
4. **New test:** "Pipeline stays alive after request completes" — send Request, get Response, send another Request
5. **New test:** "Dispose waits for pending re-injection" — trigger Retry, Dispose while Retry runs, Response still arrives
6. **New test:** "PendingWorkTracker.OnDrained fires on transition to zero" — increment, decrement, verify callback
7. **New test:** "KillSwitch shutdown drains pipeline cleanly" — in-flight Request is still processed

### Log Verification:
- NO "force-completing after pending-work retries" messages
- NO `Task.Delay` in GroupByHostKeyStage
- Instead: "Waiting for pending work to drain before shutdown" -> "Pipeline drained, stopping instance"
