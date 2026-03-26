<!-- maggus-id: 20260326-073520-feature-001 -->

# Feature 001: Actor-Based Client Lifecycle for HTTP/1.0 Deadlock Fix

## Introduction

Refactor TurboHttp client architecture to separate concerns between lifecycle management and stream I/O orchestration using two new Akka actors: `ClientStreamOwner` and `ClientStreamInstance`. This fixes the HTTP/1.0 re-injection deadlock where feature BidiStages (Retry, Cache, Compression) attempt to re-inject requests into substreams that have been prematurely completed by `GroupByHostKeyStage`.

### Architecture Context

- **Components involved**: `TurboClientStreamManager`, `GroupByHostKeyStage`, feature BidiStages (Retry, Cache, Compression, ContentEncoding), `Engine`
- **New components**: `ClientStreamOwner` actor, `ClientStreamInstance` actor, pending-work signaling protocol
- **Vision alignment**: Improves reliability and graceful shutdown — core quality requirement for production HTTP client
- **Breaking changes**: Minimal internal; public API evolved to expose actor-based design but maintains backward compatibility through wrapper

## Goals

- **Fix HTTP/1.0 deadlock** — Ensure feature stages can re-inject requests without substream premature completion
- **Improve graceful shutdown** — Owner coordinates stream lifecycle; waits for pending work before terminating
- **Add actor-based supervision** — Owner supervises stream instance; retries on transient failures
- **Expose actor-based API** — Public API leans toward actors; allows advanced users to customize supervision/retry
- **Maintain compatibility** — Existing client code continues to work; new patterns available for opt-in

## Tasks

### TASK-001-001: Define Actor Protocol and Message Contracts

**Description:** As an architect, I want to define the message contracts and interfaces between `ClientStreamOwner` and `ClientStreamInstance` so that the implementation has clear boundaries and communication semantics.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-001-002, TASK-001-003, TASK-001-004
**Parallel:** yes — foundational, unblocks all implementation tasks

**Acceptance Criteria:**
- [x] Define `ClientStreamOwner.Message` union type with message cases: `CreateStreamInstance`, `StreamInstanceCreated`, `StreamInstanceFailed`, `PendingWorkSignal`, `RequestStreamIdle`, `Shutdown`
- [x] Define `ClientStreamInstance.Message` union type with: `InitializeStream`, `StreamInitialized`, `StreamFailed`, `PendingWorkChanged`, `RequestShutdown`
- [x] Define `IPendingWorkTracker` interface — methods `IncrementPending()`, `DecrementPending()`, `IsPending: bool`
- [x] Document per-message semantics: what triggers it, what sender expects in return, who is responsible for cleanup
- [x] Create message flow diagram showing happy path (create → initialize → idle) and error paths (crash → retry)
- [x] Add to `TurboHttp/Client/` as `ActorProtocol.cs`

---

### TASK-001-002: Implement ClientStreamOwner Actor

**Description:** As an infrastructure component, I want to implement the `ClientStreamOwner` actor so that it manages the client's stream lifecycle, tracks pending work, supervises the instance, and handles retries on failure.

**Token Estimate:** ~60k tokens
**Predecessors:** TASK-001-001
**Successors:** TASK-001-006, TASK-001-007
**Parallel:** no — depends on protocol definition
**Model:** opus — complex state machine with supervision and retry logic

**Acceptance Criteria:**
- [x] `ClientStreamOwner` implements actor receive for all message types from `ActorProtocol`
- [x] Maintains state: `_pending: int` (count of re-injections from feature stages), `_streamInstance: IActorRef`, `_retryAttempts: int`, `_lastError: Exception`
- [x] On `CreateStreamInstance` → spawns child `ClientStreamInstance` actor with `SupervisorStrategy.AllForOneStrategy` (max 3 retries with exponential backoff: 100ms, 500ms, 2s)
- [x] On `PendingWorkSignal(increment)` → updates `_pending` counter; tracks which stages have pending work (debug logging)
- [x] On `StreamInstanceFailed(error)` → applies retry backoff; recreates child after delay; emits diagnostic log with attempt number
- [x] On `RequestStreamIdle` from instance → checks if `_pending == 0`; only then signals instance to complete; otherwise waits for pending work to finish
- [x] On `Shutdown` → completes request channel, waits up to 5 seconds for stream instance to drain, then terminates gracefully
- [x] File: `TurboHttp/Client/ClientStreamOwner.cs`
- [x] Diagnostic logging at INFO level: creation, retry attempts, pending work state changes

---

### TASK-001-003: Implement ClientStreamInstance Actor

**Description:** As an infrastructure component, I want to implement the `ClientStreamInstance` actor so that it owns the Akka.Streams pipeline, manages materialization, and reports completion/failure back to the owner.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-001-001
**Successors:** TASK-001-006, TASK-001-007
**Parallel:** no — depends on protocol definition
**Model:** opus — complex stream lifecycle and error handling

**Acceptance Criteria:**
- [x] `ClientStreamInstance` receives `InitializeStream` message containing: `ConnectionPool`, `TurboClientOptions`, `requestOptionsFactory`, `PipelineDescriptor`
- [x] Materializes the full Engine flow (same as current `TurboClientStreamManager`): `ChannelSource → Engine → ChannelSink`
- [x] Exposes `ChannelWriter<HttpRequestMessage> Requests` and `ChannelReader<HttpResponseMessage> Responses` as actor properties
- [x] On stream sink completion (success or failure) → sends `StreamFailed(exception)` or completion signal to parent Owner
- [x] Before allowing substream completion in `GroupByHostKeyStage`, checks with Owner: "is pending work zero?" (via `RequestStreamIdle` message)
- [x] Implements `PostStop()` → disposes materializer, closes channels, disposes pool
- [x] Diagnostic logging at DEBUG level: stream creation, frame counts, completion reasons
- [x] File: `TurboHttp/Client/ClientStreamInstance.cs`

---

### TASK-001-004: Refactor Feature BidiStages to Emit Pending-Work Signals

**Description:** As a feature developer, I want feature BidiStages (Retry, Cache, Compression, ContentEncoding) to explicitly signal pending re-injections so that the Owner knows not to complete the stream prematurely.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-001-001
**Successors:** TASK-001-005, TASK-001-007
**Parallel:** yes — can run alongside TASK-001-002, TASK-001-003

**Acceptance Criteria:**
- [x] Add `IPendingWorkTracker` field to each feature BidiStage: `RetryBidiStage`, `CacheBidiStage`, `ContentEncodingBidiStage`, `RequestCompressionBidiStage`
- [x] On `onPush(response)` where the stage decides to re-inject a request:
  - [x] Call `_pendingWorkTracker.IncrementPending()` **before** pushing the request back upstream
  - [x] After request flows downstream and response returns, call `_pendingWorkTracker.DecrementPending()`
- [x] `RetryBidiStage`: increment on deciding to retry (e.g., after 503, max retries not exceeded)
- [x] `CacheBidiStage`: increment on deciding to revalidate (e.g., 304 or cache stale)
- [x] `ContentEncodingBidiStage` (decompression): increment only if re-injection is triggered by error (rare)
- [x] `RequestCompressionBidiStage`: typically no re-injection needed; leave as-is
- [x] Add optional `_logger` field for DEBUG level logging: "pending work incremented to X", "pending work decremented to Y"
- [x] Tests: unit tests verify `IncrementPending()` / `DecrementPending()` are called at correct points in stage lifecycle
- [x] Update `ProtocolCoreGraphBuilder` to inject `IPendingWorkTracker` into feature stages during Engine construction

---

### TASK-001-005: Update GroupByHostKeyStage to Respect Pending-Work Signals

**Description:** As a routing component, I want `GroupByHostKeyStage` to check with the `IPendingWorkTracker` before completing substreams so that feature re-injections don't race against substream death.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-001-004
**Successors:** TASK-001-007
**Parallel:** no — depends on pending-work signals

**Acceptance Criteria:**
- [x] Add `IPendingWorkTracker` field to `GroupByHostKeyStage`
- [x] In the substream completion logic, before completing a substream:
  - [x] Check `_pendingWorkTracker.IsPending`
  - [x] If `true` → **do not complete yet**; re-schedule completion check in 10ms (via `GetAsyncCallback` or actor `ask`)
  - [x] If `false` → proceed with completion
- [x] Add exponential backoff cap: max 5 retries (50ms total wait) before force-completing with error
- [x] Diagnostic logging: "substream completion delayed due to pending work", "substream completed after pending work cleared"
- [x] Tests: unit test verifies that re-injections can flow through while substream is alive; no premature completion

---

### TASK-001-006: Integrate New Actors into Client Initialization

**Description:** As a builder, I want to integrate `ClientStreamOwner` and `ClientStreamInstance` into the client initialization so that all TurboHttpClient instances use the new actor-based lifecycle by default.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-001-002, TASK-001-003
**Successors:** TASK-001-008
**Parallel:** no — integration point

**Acceptance Criteria:**
- [x] Refactor `TurboClientStreamManager`: keep as internal wrapper around `ClientStreamOwner` for backward compatibility
- [x] Create `TurboClientStreamManager(options, requestFactory, system)` → spawns `ClientStreamOwner` actor internally
- [x] Expose `ChannelWriter<HttpRequestMessage>` and `ChannelReader<HttpResponseMessage>` by forwarding to underlying actor's channels
- [x] `Dispose()` → sends `Shutdown` message to owner; waits for actor termination
- [x] Update `TurboHttpClientBuilder` to pass `ActorSystem` through to `TurboClientStreamManager`
- [x] Update `TurboHttpClient` initialization to create owner+instance actors during `SendAsync` initialization
- [x] Ensure `TurboClientStreamManager` is registered in DI as singleton
- [x] Tests: integration test verifies `TurboHttpClient.SendAsync()` creates actors and completes them on `Dispose()`

---

### TASK-001-007: Add Unit Tests for Owner/Instance Lifecycle

**Description:** As a test author, I want comprehensive unit tests for the new actor lifecycle so that state transitions, retry logic, and pending-work coordination are verified.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-001-002, TASK-001-003, TASK-001-004, TASK-001-005
**Successors:** TASK-001-009
**Parallel:** yes — can run alongside TASK-001-006

**Acceptance Criteria:**
- [x] `ClientStreamOwnerTests.cs`:
  - [x] Owner creates stream instance on `CreateStreamInstance` message
  - [x] Owner increments/decrements pending work on signals
  - [x] Owner retries child creation on failure (3 attempts with backoff)
  - [x] Owner delays stream completion while pending work exists
  - [x] Owner terminates gracefully on `Shutdown` within 5 seconds
  - [x] Tests use `TestKit` (Akka.TestKit.Xunit)
- [x] `ClientStreamInstanceTests.cs`:
  - [x] Instance materializes stream on `InitializeStream`
  - [x] Instance sends failure message to parent on stream sink failure
  - [x] Instance checks pending work before allowing completion
  - [x] Instance cleans up resources on `PostStop`
- [x] `PendingWorkSignalTests.cs` (for feature stages):
  - [x] `RetryBidiStage` increments on deciding to retry
  - [x] `CacheBidiStage` increments on cache revalidation
  - [x] Pending work counter reflects accurate count
- [x] All tests have explicit `[Fact(Timeout = 5000)]` or `CancellationToken` timeouts
- [x] File: `src/TurboHttp.Tests/Client/ClientStreamOwnerTests.cs`, `ClientStreamInstanceTests.cs`, `PendingWorkSignalTests.cs`

---

### TASK-001-008: Add Integration Tests for Deadlock Scenarios

**Description:** As a quality gate, I want integration tests that reproduce the original HTTP/1.0 deadlock scenarios and verify they no longer occur.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-001-006
**Successors:** TASK-001-010
**Parallel:** yes — can run alongside TASK-001-007

**Acceptance Criteria:**
- [x] Create test class `Http10ReinjectionTests.cs` in `src/TurboHttp.IntegrationTests/`
- [x] Test 1: Retry after error — sends request that gets 503, retries successfully; verifies no deadlock with 100 iterations
- [x] Test 2: Cache revalidation — sends request, cache hit with stale marker, revalidates with If-None-Match, gets 304; verifies no deadlock with 100 iterations
- [x] Test 3: Compression negotiation — sends request with Accept-Encoding, re-negotiates compression on second request; verifies no deadlock with 100 iterations
- [x] Test 4: Mixed features — one request triggers Retry, next triggers Cache revalidation, third triggers compression; verifies pipeline stays alive across all three
- [x] Each test uses Kestrel test fixture with real TCP connections
- [x] Each test runs 100 times in a loop; captures any timeout/deadlock as failure
- [x] Tests have `[Fact(Timeout = 60000)]` — 60 second timeout per test (100 iterations)
- [x] Diagnostic output: log pending work counter state during execution

---

### TASK-001-009: Update Public API Surface and Expose Actors

**Description:** As an API designer, I want to expose the new actor-based architecture through the public API so that advanced users can customize supervision and retry behavior.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-001-007
**Successors:** TASK-001-011
**Parallel:** no — requires testing validation first

**Acceptance Criteria:**
- [x] Create `IClientStreamOwner` interface in public API (`TurboHttp.Client`):
  - [x] `Task<StreamInitializationResult> InitializeStreamAsync(StreamInitializationOptions options, CancellationToken ct)`
  - [x] `IActorRef ActorRef { get; }`
- [x] Create `StreamInitializationOptions` record: `ConnectionPool`, `TurboClientOptions`, `RequestOptionsFactory`, `SupervisorStrategy` (optional, defaults to AllForOne 3 retries)
- [x] Create `StreamInitializationResult` union type: `Success(IActorRef)`, `Failed(Exception)`
- [x] Update `TurboHttpClientBuilder` to accept optional `SupervisorStrategy customStrategy` parameter
- [x] Add deprecation notice (if any) to `TurboClientStreamManager` suggesting the new pattern for advanced use
- [x] Update `CLAUDE.md` section "Client Layer" with new actor-based design
- [x] Tests: verify `IClientStreamOwner` is accessible and usable from public API

---

### TASK-001-010: Verify Deadlock Fix with Stress Tests

**Description:** As a validation engineer, I want to run stress tests that confirm the deadlock is completely eliminated under high load and concurrency.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-001-008
**Successors:** TASK-001-011
**Parallel:** yes — can run alongside TASK-001-009

**Acceptance Criteria:**
- [ ] Run existing `Http10ReinjectionTests` 10 times sequentially (1,000 total iterations)
- [ ] Run existing H10 integration tests (78 tests) 5 times sequentially with `TURBO_DEBUG=1` enabled
- [ ] Capture diagnostic logs; verify zero deadlock timeouts, zero "completing all N substreams" without prior re-injection completion
- [ ] Create summary report: "0 deadlocks observed in 1,000 iterations" with timestamp and environment info
- [ ] Save to `.maggus/feature_001_stress_test_report.md`

---

### TASK-001-011: Documentation and Examples

**Description:** As a documentation author, I want to document the new actor-based client architecture so that users understand the lifecycle, supervision, and how to customize it.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-001-009, TASK-001-010
**Successors:** none
**Parallel:** no — final deliverable

**Acceptance Criteria:**
- [ ] Update `CLAUDE.md`:
  - [ ] Add "Actor-Based Client Lifecycle" section under "Client Layer"
  - [ ] Diagram: `ClientStreamOwner` → supervises → `ClientStreamInstance`
  - [ ] Explain pending-work signals: how features increment/decrement, how Owner uses it
  - [ ] Explain retry strategy: exponential backoff, max 3 attempts, limits
  - [ ] Explain graceful shutdown: owner waits for pending work, 5 second timeout
- [ ] Create example: `docs/examples/CustomSupervisorStrategy.cs` showing how to customize `SupervisorStrategy` on builder
- [ ] Create example: `docs/examples/MonitorPendingWork.cs` showing how to log pending work state for diagnostics
- [ ] Add section "HTTP/1.0 Reconnection and Re-Injection" explaining how Feature stages handle pending work
- [ ] Update README.md if relevant (mention actor-based architecture in "Features" section)
- [ ] Verify all code examples compile and run correctly

---

## Task Dependency Graph

```
TASK-001-001 (define contracts)
    ├─→ TASK-001-002 (ClientStreamOwner)
    ├─→ TASK-001-003 (ClientStreamInstance)
    └─→ TASK-001-004 (feature stage signals)
        └─→ TASK-001-005 (GroupByHostKeyStage update)
            └─→ TASK-001-007 (unit tests) ──→ TASK-001-009 (API)
                                              └─→ TASK-001-011 (documentation)

TASK-001-002 & TASK-001-003
    └─→ TASK-001-006 (integration)
        └─→ TASK-001-008 (integration tests) ──→ TASK-001-010 (stress tests)
                                                └─→ TASK-001-011 (documentation)
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-001-001 | ~15k | none | yes | — |
| TASK-001-002 | ~60k | 001 | no | opus |
| TASK-001-003 | ~50k | 001 | no | opus |
| TASK-001-004 | ~40k | 001 | yes (with 002, 003) | — |
| TASK-001-005 | ~25k | 004 | no | — |
| TASK-001-006 | ~30k | 002, 003 | no | opus |
| TASK-001-007 | ~50k | 002, 003, 004, 005 | yes (with 006) | — |
| TASK-001-008 | ~40k | 006 | yes (with 007) | — |
| TASK-001-009 | ~20k | 007 | no | — |
| TASK-001-010 | ~15k | 008 | yes (with 009) | — |
| TASK-001-011 | ~25k | 009, 010 | no | — |

**Total estimated tokens:** ~370k

---

## Functional Requirements

- **FR-1:** Feature BidiStages must explicitly signal pending re-injections to the owner before pushing requests upstream
- **FR-2:** `GroupByHostKeyStage` must not complete substreams while pending work is non-zero
- **FR-3:** `ClientStreamOwner` must track pending work count and only allow stream completion when count reaches zero
- **FR-4:** On stream instance crash, `ClientStreamOwner` must retry with exponential backoff (100ms, 500ms, 2s) for a maximum of 3 attempts
- **FR-5:** Graceful shutdown must complete request channel, wait up to 5 seconds for pending work to drain, then terminate
- **FR-6:** Diagnostic logging must track pending work state, retry attempts, and stream lifecycle events at INFO/DEBUG levels
- **FR-7:** Public API must expose `IClientStreamOwner` and allow users to customize `SupervisorStrategy` via `TurboHttpClientBuilder`

---

## Non-Goals

- No changes to request/response message formats or wire protocols
- No support for connection pooling strategy changes in this iteration (pool remains LRU + idle eviction)
- No changes to encoder/decoder logic or RFC compliance
- No UI or documentation site changes (CLAUDE.md only)
- No support for custom pending-work trackers — use the built-in counter-based implementation
- No metrics/telemetry integration beyond diagnostic logging

---

## Design Considerations

**Actor Model Choice:**
- `ClientStreamOwner` and `ClientStreamInstance` use the Akka actor model for fault tolerance and supervision
- Actors communicate via type-safe messages defined in `ActorProtocol`
- Supervision strategy: `AllForOneStrategy` with max 3 retries; exponential backoff
- Optional user-provided `SupervisorStrategy` via builder for advanced use cases

**Pending-Work Signaling:**
- Feature BidiStages explicitly call `_pendingWorkTracker.IncrementPending()` / `DecrementPending()`
- Owner polls pending work state via `RequestStreamIdle` message before allowing substream completion
- Exponential backoff (max 50ms) prevents busy-looping on completion checks

**Backward Compatibility:**
- `TurboClientStreamManager` remains as internal wrapper; no breaking changes to public API
- Existing code using `TurboHttpClient` and `TurboHttpClientFactory` continues to work unchanged
- New actor-based patterns available through `IClientStreamOwner` interface for opt-in advanced use

**Graceful Shutdown:**
- `Dispose()` sends `Shutdown` message to owner
- Owner closes request channel, waits up to 5 seconds for pending work to finish
- If timeout expires, owner force-terminates stream instance
- Materializer cleanup happens in `ClientStreamInstance.PostStop()`

---

## Technical Considerations

**Architecture Fit:**
- Respects existing component boundaries: Engine, routing stages, feature BidiStages
- New actors integrate via `ProtocolCoreGraphBuilder` which injects `IPendingWorkTracker` into feature stages
- No changes to Transport layer or connection pool implementation

**Performance:**
- Actor message overhead: negligible for request-per-call model (one message per lifecycle)
- Pending work checks: ~1µs per check; no hot-path impact
- Retry backoff: 100ms minimum; impacts only on stream crash (rare)

**Testing Strategy:**
- Unit tests use `Akka.TestKit.Xunit` for actor testing
- Integration tests use existing Kestrel fixtures with real TCP
- Stress tests run 100+ iterations to verify race condition elimination
- All tests include explicit timeouts

**CLAUDE.md Updates:**
- Add "Actor-Based Client Lifecycle" subsection under "Client Layer"
- Explain supervisor strategy, pending-work signals, graceful shutdown
- Link to examples

---

## Success Metrics

- **Zero deadlocks** — existing H10 integration tests (78 tests) run 5+ times with 0 deadlock timeouts
- **Graceful shutdown** — clients can be disposed in any order without hanging or resource leaks
- **Backward compatible** — all existing code using `TurboHttpClient` continues to work
- **Exposed patterns** — advanced users can customize `SupervisorStrategy` and monitor pending work state
- **Documented** — CLAUDE.md and code examples clearly explain the actor-based architecture

---

## Open Questions

None — all clarifying questions resolved by user answers (1B, 2A, 3A, 4C, 5A).

---

## Next Steps

This feature is ready for implementation. Recommend executing in this order:

1. **Phase 1 (Foundation):** TASK-001-001 (define contracts)
2. **Phase 2 (Core, parallel):** TASK-001-002, 003, 004 (actors + feature signals)
3. **Phase 3 (Integration):** TASK-001-005, 006 (GroupByHostKey + client init)
4. **Phase 4 (Testing, parallel):** TASK-001-007, 008 (unit + integration tests)
5. **Phase 5 (Completion):** TASK-001-009, 010, 011 (API, stress tests, docs)

Estimated total time: **5-7 focused sessions** (with parallelism, agents can reduce to 3-4 wall-clock sessions).
