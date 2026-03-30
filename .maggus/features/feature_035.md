<!-- maggus-id: ea2c346a-f83b-4c37-95c3-dfb33e87da26 -->
# Feature 035: Integration Test Inter-Collection Parallelism

## Introduction

The integration test suite (49 classes, 5 collections) currently runs fully sequentially. `parallelizeTestCollections: false` in `xunit.runner.json` prevents any parallelism — even though the 5 collections (H10, H11, H2, H3, TLS) are inherently independent and test different HTTP versions on different ports.

### Architecture Context

- **Vision alignment:** Pure test infrastructure improvement, no impact on product architecture.
- **Components involved:** `TurboHttp.IntegrationTests/` — test infrastructure only.
- **New patterns:** None. Only xUnit configuration and collection definitions.
- **Architecture update needed:** No.

## Goals

- Run the 5 collections (H10, H11, H2, H3, TLS) in parallel (~3-5x speedup expected)
- Move `LoggingBridgeTests` into its own isolated collection that never runs concurrently
- No changes to production code or test logic
- No new flakiness introduced

## Tasks

### TASK-035-001: Enable inter-collection parallelism in xunit.runner.json

**Description:** As a developer, I want the 5 integration test collections to run in parallel so that the full test suite finishes significantly faster.

**Token Estimate:** ~5k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside TASK-035-002
**Model:** haiku

**Acceptance Criteria:**
- [ ] `parallelizeTestCollections` is set to `true`
- [ ] `parallelizeAssembly` remains `false`
- [ ] `dotnet test` completes without errors (at least once)
- [ ] All tests green

---

### TASK-035-002: Move LoggingBridgeTests into an isolated collection

**Description:** As a developer, I want `LoggingBridgeTests` to run in its own serialized collection so that the static `LoggingLogger.LoggerFactory` field never conflicts with concurrent test executions.

**Background:** `LoggingBridgeTests` mutates `LoggingLogger.LoggerFactory` (a static field on the Akka logging bridge). It currently lives in collection `"H11"`, which is safe as long as intra-collection execution is sequential. Isolating it ensures this hidden race-condition risk cannot surface in any future parallelization step.

**Token Estimate:** ~10k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside TASK-035-001
**Model:** haiku

**Acceptance Criteria:**
- [ ] `Shared/Collections.cs` contains a new `[CollectionDefinition("Logging", DisableParallelization = true)]` class
- [ ] `LoggingBridgeTests.cs` carries `[Collection("Logging")]` instead of `[Collection("H11")]`
- [ ] `LoggingBridgeTests` passes successfully
- [ ] No other test class belongs to `"Logging"` — the collection contains exactly 1 class

## Task Dependency Graph

```
TASK-035-001 ──→ (done)
TASK-035-002 ──→ (done)
```

Both tasks are fully independent and can be executed in parallel.

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-035-001 | ~5k | none | yes (with 002) | haiku |
| TASK-035-002 | ~10k | none | yes (with 001) | haiku |

**Total estimated tokens:** ~15k

## Functional Requirements

- FR-1: `xunit.runner.json` must have `"parallelizeTestCollections": true`
- FR-2: `"parallelizeAssembly"` must remain `false`
- FR-3: A new xUnit collection definition `"Logging"` with `DisableParallelization = true` must exist in `Shared/Collections.cs`
- FR-4: `LoggingBridgeTests` must belong to the `"Logging"` collection
- FR-5: All other test classes retain their existing `[Collection(...)]` attributes unchanged
- FR-6: The full test suite (`dotnet test --project TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj`) must be green after the changes

## Non-Goals

- No intra-collection parallelism (no removal of `[Collection]` attributes)
- No changes to `ServerFixture`, `ActorSystemFixture`, or `ClientHelper`
- No changes to `Routes.cs` or other shared infrastructure classes
- No refactoring of `LoggingBridgeTests` itself — only its collection assignment changes
- No tuning of `ThreadPool.SetMinThreads` (512 is sufficient)

## Technical Considerations

### Why is inter-collection parallelism safe?

| Shared State | Risk | Reasoning |
|---|---|---|
| `ServerFixture` (Kestrel) | None | Kestrel is natively concurrent; all ports are OS-assigned (port 0) |
| `ActorSystemFixture` | None | Akka `ActorSystem` is thread-safe and designed for concurrent streams |
| `Routes._retryCounters` | None | `ConcurrentDictionary`; all tests use `Guid.NewGuid()` as key |
| `LoggingLogger.LoggerFactory` | None (after TASK-035-002) | Only mutated by `LoggingBridgeTests`, which runs isolated |

### Port sharing is not a problem

H10 and H11 share `HttpPort`; H3 and TLS share `HttpsPort`. This is fine — Kestrel handles concurrent connections on the same port natively. Each test opens its own TCP connection via its own `ClientHelper` instance.

### Critical files

- `src/TurboHttp.IntegrationTests/xunit.runner.json`
- `src/TurboHttp.IntegrationTests/Shared/Collections.cs`
- `src/TurboHttp.IntegrationTests/LoggingBridgeTests.cs`

## Success Metrics

- Total integration test suite runtime reduced by at least 50% (target: ~3-5x speedup)
- No previously passing test turns red
- 0 new flaky failures across 3 consecutive runs

## Open Questions

*None.*
