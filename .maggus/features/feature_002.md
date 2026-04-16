<!-- maggus-id: 04843a16-38a9-43d6-a151-1ac8f7513053 -->

# Feature 002: Acceptance Test Infrastructure — BehaviorStack, ActivityLog, Shared Harness

## Introduction

Feature 001 migrated all 84 integration tests to deterministic StreamTests/Acceptance. The migration succeeded but left behind significant boilerplate duplication (~80 copies of SendScriptedAsync) and missed two key pillars from the "remote testing without integration tests" blueprint: **Behavior Stacks** (composable error injection) and **Activity Logging** (structured operation recording).

This feature adds those missing pillars and extracts the duplicated test harness code into shared helpers.

### Architecture Context

- **Architecture alignment:** Extends the existing `Acceptance/Shared/` test infrastructure from Feature 001. All new code lives in the test project — zero production code changes.
- **Components involved:** `TurboHTTP.StreamTests/Acceptance/Shared/` (new files + one modified file)
- **Existing patterns extended:** `ScriptedFakeConnectionStage`, `ResponseMapFake`, `EngineTestBase`
- **Blueprint alignment:** Implements Pillar 3 (Behavior Stacks) and Pillar 4 (Activity Logging) from the blueprint in `remote-testing-without-integration-tests.md`

## Goals

- Enable composable, deterministic error injection via a `BehaviorStack` that supports push/pop/delay/error behaviors
- Enable structured observation of test transport operations via a typed `ActivityLog`
- Eliminate ~80 copies of duplicated `SendScriptedAsync`/`SendAsync` pipeline boilerplate
- Keep all changes opt-in — zero modifications to existing acceptance tests

## Tasks

### TASK-002-001: BehaviorStack — Composable Error/Delay Injection
**Description:** As a test author, I want a generic `BehaviorStack<TIn, TOut>` so that I can compose error injection, delays, and custom behaviors without ad-hoc requestIndex switches in my response factories.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-002-004
**Parallel:** yes — can run alongside TASK-002-002, TASK-002-003

**Acceptance Criteria:**
- [ ] `BehaviorStack<TIn, TOut>` class in `Acceptance/Shared/BehaviorStack.cs`
- [ ] `Push(Func<TIn, TOut>)` adds behavior on top of stack
- [ ] `PushConstant(TOut)` always returns the same value
- [ ] `PushError(Exception)` throws on Apply
- [ ] `PushDelayed()` returns a `DelayGate` with `Release(TOut)`/`Fault(Exception)` methods
- [ ] `PushOnce(Func<TIn, TOut>)` auto-pops after single invocation
- [ ] `Pop()` removes topmost behavior
- [ ] `Apply(TIn)` executes topmost behavior, falls through to default if empty
- [ ] Unit tests in `BehaviorStackSpec.cs` covering all operations including nesting
- [ ] Build passes

---

### TASK-002-002: ActivityLog — Structured Operation Recording
**Description:** As a test author, I want a typed `ActivityLog` that records transport operations (writes, disconnects, aborts, responses) so that I can assert on retry counts, disconnect events, and operation ordering.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-002-004
**Parallel:** yes — can run alongside TASK-002-001, TASK-002-003

**Acceptance Criteria:**
- [ ] `ActivityLog` class in `Acceptance/Shared/ActivityLog.cs`
- [ ] `Record(Activity)` appends typed activity event
- [ ] `Entries` property returns all recorded activities in chronological order
- [ ] `OfType<T>()` filters by activity subtype
- [ ] `Clear()` resets the log
- [ ] Activity record types: `WriteAttempt(int Index, byte[] Payload)`, `DisconnectEvent(string Reason)`, `ConnectionAbort()`, `ResponseDelivered(int Index, int ByteCount)`
- [ ] All activity types are immutable records with `DateTimeOffset Timestamp`
- [ ] Unit tests in `ActivityLogSpec.cs` covering recording, filtering, ordering
- [ ] Build passes

---

### TASK-002-003: AcceptanceHarness — Shared Send Helpers
**Description:** As a test author, I want shared extension methods on `EngineTestBase` that replace the 80+ copies of `SendScriptedAsync`/`SendAsync` boilerplate so that each test method can be 2-3 lines instead of 12-15.

**Token Estimate:** ~50k tokens
**Predecessors:** none
**Successors:** none (adoption is separate effort)
**Parallel:** yes — can run alongside TASK-002-001, TASK-002-002

**Acceptance Criteria:**
- [ ] `AcceptanceHarness.cs` in `Acceptance/Shared/` with extension methods on `EngineTestBase`
- [ ] `SendScriptedAsync(engine, request, factory)` — replaces the 12-line byte-level pattern
- [ ] `SendScriptedManyAsync(engine, requests, factory, expectedCount)` — multi-request variant
- [ ] `SendWithMapAsync(featureStage, map, request)` — replaces the 9-line feature-logic pattern
- [ ] `SendWithHandlersAsync(handlers, map, request)` — replaces the 15-line handler pipeline pattern
- [ ] Optional `ActivityLog?` parameter on all methods for observable runs
- [ ] Smoke tests in `AcceptanceHarnessSpec.cs` proving each helper produces the same result as the inline code it replaces
- [ ] Build passes, no existing tests broken

---

### TASK-002-004: Wire BehaviorStack + ActivityLog into ScriptedFakeConnectionStage
**Description:** As a test author, I want `ScriptedFakeConnectionStage` to optionally accept a `BehaviorStack` and `ActivityLog` so that error injection and observation work at the byte-level transport fake.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-002-001, TASK-002-002
**Successors:** none
**Parallel:** no — depends on both infrastructure tasks

**Acceptance Criteria:**
- [ ] New constructor overload: `ScriptedFakeConnectionStage(factory, behaviorStack?, activityLog?)`
- [ ] Existing constructor unchanged (no breaking change)
- [ ] When `BehaviorStack` provided, pushed behaviors override the response factory
- [ ] When `ActivityLog` provided, records `WriteAttempt`, `ResponseDelivered`, `ConnectionAbort` events
- [ ] All existing `ScriptedFakeConnectionStageSpec` tests still pass
- [ ] New tests verify BehaviorStack integration (push error → stage fails, push once → first fails/second succeeds)
- [ ] New tests verify ActivityLog integration (run pipeline → log contains expected events)
- [ ] Build passes

---

## Task Dependency Graph

```
TASK-002-001 (BehaviorStack) ──┐
                                ├──→ TASK-002-004 (Wire into ScriptedFake)
TASK-002-002 (ActivityLog)   ──┘
TASK-002-003 (AcceptanceHarness) ────  (independent)
```

| Task | Title | Estimate | Predecessors | Parallel |
|------|-------|----------|--------------|----------|
| TASK-002-001 | BehaviorStack | ~40k | none | yes (with 002, 003) |
| TASK-002-002 | ActivityLog | ~25k | none | yes (with 001, 003) |
| TASK-002-003 | AcceptanceHarness | ~50k | none | yes (with 001, 002) |
| TASK-002-004 | Wire into ScriptedFake | ~40k | 001, 002 | no |

**Total estimated tokens:** ~155k

## Functional Requirements

- FR-1: BehaviorStack must support Push/PushOnce/PushConstant/PushError/PushDelayed/Pop operations
- FR-2: PushDelayed must provide a gate with Release/Fault methods for deterministic timing control
- FR-3: ActivityLog must record typed, timestamped events in chronological order
- FR-4: ActivityLog must support LINQ-style filtering via `OfType<T>()`
- FR-5: AcceptanceHarness must provide helpers for all 3 major pipeline patterns (scripted byte-level, ResponseMap feature-level, handler pipeline)
- FR-6: All new infrastructure must be opt-in — existing tests must not be modified or broken
- FR-7: ScriptedFakeConnectionStage must accept optional BehaviorStack and ActivityLog without breaking existing constructor

## Non-Goals

- Migrating existing 90+ test files to use the new infrastructure (separate future effort)
- Adding BehaviorStack/ActivityLog to `ResponseMapFake` or `FakeProxyStage` (can be done later)
- Adding BehaviorStack/ActivityLog to H2/H3 engine fake stages (different shape)
- Performance optimization of test infrastructure
- Changing any production code in TurboHTTP

## Technical Considerations

- All new classes in namespace `TurboHTTP.StreamTests.Acceptance.Shared`
- BehaviorStack does NOT need thread safety — Akka stage confinement guarantees single-thread access
- `DelayGate` uses `TaskCompletionSource` internally. In `ScriptedFakeConnectionStage`, the async callback pattern (`GetAsyncCallback`) bridges the TCS completion into the stage's execution context
- Extension methods on `EngineTestBase` require the class to remain `public` (it already is)
- Port naming convention: no new GraphStage ports expected, but if any are added they must follow `StageName.Direction` pattern

## Success Metrics

- Zero existing test failures after all 4 tasks complete
- BehaviorStack enables "first N requests fail, then succeed" pattern in 2 lines instead of 10+
- ActivityLog enables "exactly 3 retry attempts were made" assertion in 1 line
- AcceptanceHarness reduces per-test boilerplate from 12-15 lines to 2-3 lines

## Open Questions

*None — design aligned with blueprint principles and existing codebase patterns.*
