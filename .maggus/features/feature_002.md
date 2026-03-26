<!-- maggus-id: 20260325-213710-feature-027 -->
# Feature 027: Diagnose and Fix HTTP/1.0 Pipeline Deadlock in Batch Test Runs

## Introduction

HTTP/1.0 integration tests deadlock sporadically when run as a batch (78 tests sequential), but pass 100% in isolation. The deadlocks manifest as 10-30s timeouts on `SendAsync` — the pipeline hangs and never produces a response. The issue affects all H10 test categories (Cookie, Cache, Retry, Redirect, Compression, Connection, Error) randomly, including **single-request tests** that don't involve reconnection.

This proves the root cause is **not** HTTP/1.0 reconnection logic (which was already fixed with `_connectionGen` guards). Instead, something in the shared pipeline infrastructure accumulates state or leaks resources across sequential test runs, eventually causing a deadlock in a subsequent test's fresh pipeline.

### Architecture Context

- **Components involved:**
  - `TurboClientStreamManager` — owns the Akka.Streams pipeline per client (ChannelSource → Engine → ChannelSink)
  - `ConnectionStage` / `TcpTransportHandler` — TCP connection lifecycle, inbound pump
  - `GroupByHostKeyStage` — per-host substream routing
  - `ExtractOptionsStage` — ConnectItem emission and reconnection signaling
  - `MergePreferred` — priority merge for ConnectItem + DataItem + ConnectionReuseItem
  - `ConnectionPool` — per-client connection pool with idle eviction
  - `ActorSystemFixture` — shared ActorSystem across all integration tests

- **Key observation:** Tests pass 10/10 in isolation (own process). They pass 78/78 sometimes in batch. When they fail, the failing test is random. This is classic accumulated-state corruption or resource exhaustion.

## Goals

- Identify the exact location of the deadlock via structured diagnostic logging
- Fix the root cause so 78/78 H10 tests pass in 10 consecutive batch runs (zero flakes)
- Implement orderly stream shutdown on `Dispose()` so no zombie actors/pumps linger
- Keep HTTP/1.0 single-client pattern (no regression to fresh-client-per-request workaround)

## Hypotheses to Investigate

Based on the analysis session, ranked by likelihood:

### H1: GroupByHostKey Substream Death (Most Likely)
`GroupByHostKey` creates substreams per host-key. If a substream's internal actor dies (e.g., from an unhandled exception in a stage), the substream is marked as dead. Subsequent requests to the same host-key in a NEW client's pipeline should get a NEW substream — but if the GroupByHostKey stage or the host-key registry has stale state in the shared ActorSystem, new clients might inherit dead substreams.

**Evidence:** Test output shows `GroupByHostKeyStage: Upstream failure absorbed: Processor actor [...] terminated abruptly` warnings before failures.

### H2: MergePreferred Demand Stall After Reconnection
After `ConnectionReuseItem` is processed and a new `ConnectItem` is emitted, the `MergePreferred` stages (`transportMerge0` and `transportMerge`) might not propagate demand correctly. If one merge input has no demand, the pipeline stalls.

**Evidence:** Retry tests (which generate pipeline-internal reconnection) fail disproportionately.

### H3: ConnectionPool Lease Leak
If `ConnectionPool.Release()` or `ConnectionLease.Dispose()` doesn't fully clean up in edge cases (e.g., double-release, race between `HandleConnectionReuseItem` and `_onInboundComplete`), subsequent `AcquireAsync` calls might block on an exhausted semaphore.

**Evidence:** Each test creates its own pool, so this should be isolated. But if `HostConnections._limiter` isn't released on all code paths, the pool deadlocks.

### H4: Inbound Pump Thread Leak
Each `TcpTransportHandler.StartInboundPump()` spawns a `Task.Run` that reads from the connection. If `StopInboundPump()` doesn't fully cancel the task (CTS cancelled after callback posted), zombie pump tasks accumulate and exhaust the ThreadPool or Akka dispatcher.

**Evidence:** 78 tests × 1-4 connections each = 78-312 pump tasks. Even with `_connectionGen` guards, the tasks stay alive until the channel reader completes.

## Tasks

### TASK-027-001: Add Structured Diagnostic Logging to Pipeline Stages
**Description:** As a developer, I want structured logging at every state transition in the HTTP/1.0 pipeline so that I can trace exactly where the deadlock occurs when a test hangs.

**Token Estimate:** ~75k tokens
**Predecessors:** none
**Successors:** TASK-027-002
**Parallel:** yes — can run alongside TASK-027-005
**Model:** opus

**Acceptance Criteria:**
- [x] `ExtractOptionsStage.Logic`: log on every `onPush` (request received), every `ConnectItem` emission, every `_needsReconnect` change, every `InReuse` signal received
- [x] `TcpTransportHandler`: log `HandleConnectItem` (with key), `HandleDataItem` (buffered vs written), `HandleConnectionReuseItem` (canReuse, _upstreamFinished), `_onLeaseAcquired` (gen), `_onInboundComplete` (gen match/mismatch), `_onOutboundWriteFailed`, `StopInboundPump`, `Cleanup`
- [x] `ConnectionStage.Logic`: log every `HandlePush` item type, every `PushOutput`, every `SignalPullInput`, every `CompleteStage`
- [x] `ConnectionReuseStage`: log signal emission (canReuse, endpoint), response push
- [x] `GroupByHostKeyStage`: log substream creation, substream completion, substream failure
- [x] All logging uses `ILoggingAdapter` (Akka's built-in) with `Debug` level — does NOT affect production performance
- [x] Logging can be enabled via Akka HOCON config: `akka.loglevel = DEBUG`
- [x] Build succeeds with zero warnings

**Implementation approach:**
Add `Log.Debug("ExtractOptions: onPush request={Uri}, _connectItemSent={Flag}, _needsReconnect={Flag}", ...)` style calls at each decision point. Use structured message format (not string interpolation) so Akka can filter efficiently.

---

### TASK-027-002: Reproduce and Capture Deadlock Trace
**Description:** As a developer, I want to run the H10 batch tests with diagnostic logging enabled and capture the exact log output when a test deadlocks, so I can identify which stage is waiting for what.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-027-001
**Successors:** TASK-027-003
**Parallel:** no — requires logging to be in place

**Acceptance Criteria:**
- [x] Create `src/TurboHttp.IntegrationTests/akka.debug.conf` with `akka.loglevel = DEBUG` and `akka.loggers = ["Akka.Event.DefaultLogger"]`
- [x] Modify `ActorSystemFixture` to optionally load this config (via env var `TURBO_DEBUG=1`)
- [x] Run the H10 batch test 5 times with `TURBO_DEBUG=1` and capture output to files
- [x] For each deadlocked test: extract the last 50 log lines before the timeout
- [x] Identify the pattern: which stage's log entry is the LAST one before the hang?
- [x] Document findings in `.maggus/analysis/feature_027_deadlock_trace.md`:
  - Which stage is waiting (last log entry)
  - What it's waiting for (demand? data? callback?)
  - What should have provided it (which upstream/downstream stage)
  - Whether the pattern is consistent across different deadlocked tests

---

### TASK-027-003: Fix the Root Cause
**Description:** As a developer, I want to fix the exact deadlock mechanism identified in TASK-027-002 so that all 78 H10 tests pass reliably in batch runs.

**Token Estimate:** ~100k tokens
**Predecessors:** TASK-027-002
**Successors:** TASK-027-004
**Parallel:** no — requires diagnosis
**Model:** opus

**Acceptance Criteria:**
- [x] Root cause identified and documented
- [x] Fix implemented in the minimal set of files
- [x] Fix does not break HTTP/1.1, HTTP/2, or HTTP/3 behavior
- [x] Build succeeds with zero warnings
- [ ] Unit test added that provokes the exact deadlock scenario (if reproducible in a stream test)
      — Not reproducible in stream test: requires real TCP connection close to kill Source.Queue actor

**Likely fix areas (based on hypotheses):**
- H1: Fix GroupByHostKey substream lifecycle or isolation
- H2: Fix MergePreferred demand after reconnection signal path
- H3: Fix ConnectionPool lease release on all code paths
- H4: Fix pump task cleanup in StopInboundPump / Cleanup

---

### TASK-027-004: Validate Zero Flakes (10x Batch Run)
**Description:** As a developer, I want to run the H10 batch tests 10 times consecutively and see zero failures across all runs, proving the fix is stable.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-027-003
**Successors:** TASK-027-006
**Parallel:** no — requires fix to be in place

**Acceptance Criteria:**
- [ ] Script: `for i in $(seq 1 10); do dotnet test ... --filter-namespace H10; done`
- [ ] 10/10 runs show `Passed! total: 78 failed: 0`
- [ ] No warnings about "terminated abruptly" or "Upstream failure absorbed" in any run
- [ ] Total execution time per run is under 15 seconds (no lingering timeouts)
- [ ] Results documented in `.maggus/analysis/feature_027_validation.md`

---

### TASK-027-005: Implement Orderly Stream Shutdown in Dispose
**Description:** As a developer, I want `TurboClientStreamManager.Dispose()` to orderly shut down the Akka.Streams pipeline so that no zombie actors or pump tasks linger in the shared ActorSystem.

**Token Estimate:** ~50k tokens
**Predecessors:** none
**Successors:** TASK-027-006
**Parallel:** yes — can run alongside TASK-027-001

**Acceptance Criteria:**
- [ ] `Dispose()` completes the request channel writer (already done via `Requests.TryComplete()`)
- [ ] `TcpTransportHandler.OnUpstreamFinished()`: when handle is active, close connection and complete stage (not just set flag)
- [ ] After `Dispose()`, all stream actors under the materializer's supervision tree are stopped within 2 seconds
- [ ] No `Task.Run` pump threads remain alive after dispose
- [ ] Verify via test: create 50 clients, dispose all, check `ActorSystem` actor count returns to baseline
- [ ] Build succeeds with zero warnings

**Implementation:**
```csharp
// TcpTransportHandler.OnUpstreamFinished:
public void OnUpstreamFinished()
{
    _upstreamFinished = true;
    // Active connection exists — close it, no more requests coming.
    _connectionGen++;
    StopInboundPump();
    if (_currentLease is { } lease)
    {
        lease.MarkNoReuse();
        ReturnLeaseToPool(canReuse: false);
    }
    _handle = null;
    _currentLease = null;
    _callbacks!.RequestCompleteStage();
}
```

Note: this is safe because `OnUpstreamFinished` only fires when the ChannelSource completes (i.e., client is being disposed). No more requests will arrive.

---

### TASK-027-006: Cleanup — Remove Diagnostic Logging from Production Code
**Description:** As a developer, I want to remove or gate the verbose diagnostic logging added in TASK-027-001 so that production code is not noisy.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-027-004, TASK-027-005
**Successors:** none
**Parallel:** no — requires validation to pass first

**Acceptance Criteria:**
- [ ] All `Log.Debug(...)` calls from TASK-027-001 are either:
  - Removed entirely (if they're too verbose), OR
  - Kept behind a `if (Log.IsDebugEnabled)` guard (if useful for future diagnosis)
- [ ] Key state transitions (ConnectItem emission, reconnect, lease acquire/release) keep one-line Debug logs
- [ ] No performance impact when logging is at INFO level (default)
- [ ] Build succeeds with zero warnings

## Task Dependency Graph

```
TASK-027-001 (logging) ──→ TASK-027-002 (reproduce) ──→ TASK-027-003 (fix) ──→ TASK-027-004 (validate)
                                                                                       ↓
TASK-027-005 (shutdown) ──────────────────────────────────────────────────────→ TASK-027-006 (cleanup)
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-027-001 | ~75k | none | yes (with 005) | opus |
| TASK-027-002 | ~40k | 001 | no | — |
| TASK-027-003 | ~100k | 002 | no | opus |
| TASK-027-004 | ~20k | 003 | no | — |
| TASK-027-005 | ~50k | none | yes (with 001) | — |
| TASK-027-006 | ~15k | 004, 005 | no | haiku |

**Total estimated tokens:** ~300k

## Functional Requirements

- FR-1: 78 H10 integration tests must pass in 10 consecutive batch runs with zero failures
- FR-2: `TurboClientStreamManager.Dispose()` must stop all stream actors and pump tasks within 2 seconds
- FR-3: No "Processor actor terminated abruptly" warnings during clean test runs
- FR-4: HTTP/1.0 reconnection (sequential `SendAsync` on same client) must work reliably
- FR-5: HTTP/1.1 and HTTP/2 tests must not regress
- FR-6: Single-client-per-test pattern retained for all H10 tests (no fresh-client-per-request workaround)

## Non-Goals

- HTTP/2 or HTTP/3 pipeline changes (unless the deadlock fix is cross-cutting)
- Performance optimization of the reconnection path
- Refactoring the pipeline to per-request streams (too large a scope)
- Making the diagnostic logging a permanent, configurable production feature

## Technical Considerations

- **Akka.Streams stage logging:** Use `Log` property (inherited from `GraphStageLogic`) which is an `ILoggingAdapter`. Messages go through the ActorSystem's logging pipeline.
- **MergePreferred semantics:** Preferred input is always checked first when output has demand. Both regular and preferred inputs are pulled eagerly. If either input is not pulled, the merge blocks.
- **GroupByHostKey:** Uses `GroupBy` under the hood with `maxSubstreams` parameter. Substreams have a finite lifecycle — completed substreams are cleaned up. But if a substream actor dies without completing, the GroupBy operator might not reopen that key.
- **ChannelSource completion propagation:** When `ChannelWriter.TryComplete()` is called, `ChannelSource` detects the channel completion and completes the Akka source. This cascades through `Via(engineFlow)` to `ChannelSink`. The cascade requires all intermediate stages to propagate completion — if any stage blocks completion (like ConnectionStage waiting for _onInboundComplete), the cascade stalls.
- **Shared ActorSystem impact:** All materializers share the same dispatcher thread pool. Zombie actors consume dispatcher slots. With 78 tests and incomplete cleanup, the dispatcher can become saturated even with `MinThreads=512`.

## Success Metrics

- 78/78 passed in 10 of 10 consecutive runs
- Average batch run time under 15 seconds (currently 10-11s when all pass)
- Zero "terminated abruptly" warnings in clean runs
- No zombie actors after test completion (verified via actor count baseline test)

## Open Questions

None — all resolved.
