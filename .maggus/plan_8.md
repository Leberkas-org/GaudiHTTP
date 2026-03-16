# Plan 8: Production-Readiness — Evolve Actor Pool (Option A)

**Date:** 2026-03-16
**Based on:** GAP_LIST.md (15 gaps), ARCHITECTURE_DECISION.md (Option A recommended)
**Architecture:** Evolve current Actor Pool incrementally — fix known gaps, add integration tests

---

## Scope

This plan covers **Critical** and **High** priority gaps (GAP-001 through GAP-008).
Medium and Low gaps (GAP-009 through GAP-015) are deferred to a future plan.

---

## Task Dependency Graph

```
TASK-P8-001 (ConnectionReuseStage wiring)     ─────────────────────┐
TASK-P8-002 (ConnectionActor reconnect fix)                        │
    └─ TASK-P8-003 (Stale queue cleanup)                           ├─→ TASK-P8-006 (Integration tests)
TASK-P8-004 (PerHostConnectionLimiter wiring)  ────────────────────┤       ↓
TASK-P8-005 (Graceful shutdown / IAsyncDisposable)                 │  TASK-P8-007 (TLS E2E tests)
                                                                   │
TASK-P8-008 (Update CLAUDE.md)  ← depends on all above ───────────┘
```

---

## User Stories

---

### TASK-P8-001: Wire `ConnectionReuseStage` into Engine Pipeline

**Description:** As a developer, I want HTTP/1.1 keep-alive decisions to flow through the production pipeline so that TCP connections are reused when the server allows it.

**Gap:** GAP-001 (Critical, S)
**Dependencies:** None

**Acceptance Criteria:**
- [ ] `ConnectionReuseStage` is wired into `BuildConnectionFlowPublic` in `Engine.cs` (after BidiFlow decode output, before response leaves the substream)
- [ ] `ConnectionReuseDecision` callback feeds back to `HostPoolActor` via a `MarkConnectionNoReuse` (or equivalent) message
- [ ] Existing `ConnectionReuseStageTests` (10 tests) remain green
- [ ] New stream test: verify `ConnectionReuseStage` is invoked when a response passes through the Engine in test mode
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/Streams/Engine.cs` — `BuildConnectionFlowPublic`
- `src/TurboHttp/Streams/Stages/ConnectionReuseStage.cs`
- `src/TurboHttp/IO/HostPoolActor.cs` — new message handler

---

### TASK-P8-002: Fix `ConnectionActor.Reconnect()` — Failure Notification + Backoff

**Description:** As a developer, I want `ConnectionActor` to notify its parent `HostPoolActor` when a TCP connection drops, and to use exponential backoff on reconnect, so that server-down scenarios don't cause a tight reconnect loop.

**Gap:** GAP-002 (Critical, M)
**Dependencies:** None

**Acceptance Criteria:**
- [ ] `ConnectionActor.Reconnect()` sends `ConnectionFailed` message to parent (`HostPoolActor`) before attempting reconnect
- [ ] Exponential backoff implemented using `PoolConfig.ReconnectInterval` as base delay
- [ ] `PoolConfig.MaxReconnectAttempts` is respected — after N failures, the actor stops reconnecting and notifies parent of permanent failure
- [ ] `_outbound` null-guarded in the `SinkRef` write lambda to prevent `NullReferenceException` during reconnect window
- [ ] New unit test: verify `ConnectionFailed` is sent to parent on TCP drop
- [ ] New unit test: verify backoff delay increases on consecutive failures
- [ ] New unit test: verify reconnect stops after `MaxReconnectAttempts`
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/IO/ConnectionActor.cs` — `Reconnect()`, `HandleDisconnected`, `HandleTerminated`
- `src/TurboHttp/IO/PoolConfig.cs` — `MaxReconnectAttempts`, `ReconnectInterval`

---

### TASK-P8-003: Stale Queue Cleanup in `HostPoolActor` on `ConnectionFailed`

**Description:** As a developer, I want `HostPoolActor` to clean up stale connection queue entries when a connection drops, so that requests are not silently routed to dead connections.

**Gap:** GAP-003 (Critical, S)
**Dependencies:** TASK-P8-002 (requires `ConnectionFailed` to be sent)

**Acceptance Criteria:**
- [ ] `HostPoolActor.HandleFailure` removes `_connectionQueues[conn.Actor]` entry for the failed connection
- [ ] Failed connection is marked `Active = false` in `_connections`
- [ ] Pending items from the dead queue are re-routed to another active connection, or buffered for the next available connection
- [ ] New unit test: verify queue entry is removed on `ConnectionFailed`
- [ ] New unit test: verify pending items are re-queued (not lost)
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/IO/HostPoolActor.cs` — `HandleFailure`, `_connectionQueues`, `_connections`

---

### TASK-P8-004: Wire `PerHostConnectionLimiter` into `HostPoolActor`

**Description:** As a developer, I want per-host connection limits enforced in the actor pool, so that the client doesn't open unlimited TCP connections to a single host.

**Gap:** GAP-006 (High, S)
**Dependencies:** None

**Acceptance Criteria:**
- [ ] `PerHostConnectionLimiter` is instantiated in `HostPoolActor` using `PoolConfig.MaxConnectionsPerHost`
- [ ] `SpawnConnection()` in `HostPoolActor` checks `PerHostConnectionLimiter.TryAcquire()` before creating a new `ConnectionActor`
- [ ] Excess requests are queued (buffered) until a connection slot becomes available
- [ ] New unit test: verify that `SpawnConnection()` is blocked when limit is reached
- [ ] New unit test: verify queued requests drain when a connection slot frees up
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/IO/HostPoolActor.cs` — `SpawnConnection()`
- `src/TurboHttp/Protocol/PerHostConnectionLimiter.cs`
- `src/TurboHttp/IO/PoolConfig.cs` — `MaxConnectionsPerHost`

---

### TASK-P8-005: Graceful Shutdown — `IAsyncDisposable` for Client and StreamManager

**Description:** As a developer, I want `TurboHttpClient` and `TurboClientStreamManager` to implement `IAsyncDisposable`, so that resources (TCP connections, actors, channels) are cleaned up on shutdown.

**Gap:** GAP-005 (High, M)
**Dependencies:** None

**Acceptance Criteria:**
- [ ] `TurboHttpClient` implements `IAsyncDisposable`
- [ ] `TurboClientStreamManager` implements `IAsyncDisposable`
- [ ] `DisposeAsync()` completes the request `ChannelWriter` (stops accepting new requests)
- [ ] `DisposeAsync()` cancels all pending `TaskCompletionSource` entries with `ObjectDisposedException`
- [ ] `DisposeAsync()` stops the `PoolRouterActor` hierarchy (via `GracefulStop` or `PoisonPill`)
- [ ] `DisposeAsync()` terminates the Akka stream materialization
- [ ] New unit test: verify `SendAsync()` after `DisposeAsync()` throws `ObjectDisposedException`
- [ ] New unit test: verify pending requests are cancelled on dispose
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp/Client/TurboHttpClient.cs`
- `src/TurboHttp/Client/TurboClientStreamManager.cs`

---

### TASK-P8-006: Integration Tests — End-to-End `SendAsync()` Against Kestrel

**Description:** As a developer, I want integration tests that prove `TurboHttpClient.SendAsync()` works end-to-end against a real HTTP server (Kestrel).

**Gap:** GAP-004 (High, M)
**Dependencies:** TASK-P8-001, TASK-P8-002, TASK-P8-003, TASK-P8-004 (core fixes make E2E meaningful)

**Acceptance Criteria:**
- [ ] Integration test class(es) created in `src/TurboHttp.IntegrationTests/`
- [ ] Tests call `TurboHttpClient.SendAsync()` (not engine-level helpers)
- [ ] Test coverage includes:
  - Basic GET and POST requests (HTTP/1.1)
  - Redirect chains (301, 302, 307, 308)
  - Cookie round-trips (set + echo)
  - Cache behavior (max-age hit, etag validation)
  - Retry on 503
  - Connection reuse (multiple requests on same TCP connection)
- [ ] HTTP/2 path tested (at minimum: basic GET via `KestrelH2Fixture`)
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp.IntegrationTests/` — new test classes
- `src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs`
- `src/TurboHttp.IntegrationTests/Shared/KestrelH2Fixture.cs`

---

### TASK-P8-007: TLS End-to-End Verification

**Description:** As a developer, I want integration tests that prove HTTPS requests work end-to-end through the full pipeline (including the actor pool path).

**Gap:** GAP-007 (High, S-M)
**Dependencies:** TASK-P8-006 (integration test infrastructure)

**Acceptance Criteria:**
- [ ] `TcpOptionsFactory` verified to produce `TlsOptions` for `https://` URIs
- [ ] Integration test: basic HTTPS GET via `TurboHttpClient.SendAsync()` against `KestrelTlsFixture`
- [ ] Integration test: HTTPS POST with body
- [ ] Certificate validation callback is exercisable via `TurboClientOptions`
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` — all tests pass

**Key Files:**
- `src/TurboHttp.IntegrationTests/Shared/KestrelTlsFixture.cs`
- `src/TurboHttp/IO/TcpOptionsFactory.cs` (or equivalent)

---

### TASK-P8-008: Update CLAUDE.md — Accurate "Current Limitations"

**Description:** As a developer, I want the CLAUDE.md "Current Limitations" section to accurately reflect the project state after all Plan 8 fixes.

**Gap:** GAP-008 (High, S)
**Dependencies:** All preceding tasks (update after fixes are complete)

**Acceptance Criteria:**
- [ ] "Current Limitations" section rewritten to reflect audit findings:
  - Remove outdated claims about unwired pipeline, uncommented graph, missing stages
  - Keep accurate remaining limitations (e.g., HTTP/3 stub, no `IHttpClientFactory`)
- [ ] Any limitations closed by Plan 8 tasks are removed
- [ ] Any new limitations discovered during Plan 8 are documented
- [ ] `dotnet build ./src/TurboHttp.sln` — 0 errors (no code changes, doc-only)

**Key Files:**
- `CLAUDE.md` — "Current Limitations" section

---

## Implementation Order

| Order | Task | Gap | Priority | Size | Parallelizable? |
|-------|------|-----|----------|------|-----------------|
| 1 | TASK-P8-001 | GAP-001 | Critical | S | ✅ Yes (with P8-002, P8-004, P8-005) |
| 1 | TASK-P8-002 | GAP-002 | Critical | M | ✅ Yes (with P8-001, P8-004, P8-005) |
| 1 | TASK-P8-004 | GAP-006 | High | S | ✅ Yes (with P8-001, P8-002, P8-005) |
| 1 | TASK-P8-005 | GAP-005 | High | M | ✅ Yes (with P8-001, P8-002, P8-004) |
| 2 | TASK-P8-003 | GAP-003 | Critical | S | ❌ Depends on P8-002 |
| 3 | TASK-P8-006 | GAP-004 | High | M | ❌ Depends on P8-001..P8-004 |
| 4 | TASK-P8-007 | GAP-007 | High | S-M | ❌ Depends on P8-006 |
| 5 | TASK-P8-008 | GAP-008 | High | S | ❌ Depends on all above |

**Phase 1** (parallel): TASK-P8-001, P8-002, P8-004, P8-005 — 4 independent fixes
**Phase 2** (sequential): TASK-P8-003 — depends on P8-002
**Phase 3**: TASK-P8-006 — integration tests (depends on Phase 1+2)
**Phase 4**: TASK-P8-007 — TLS tests (depends on P8-006)
**Phase 5**: TASK-P8-008 — documentation update (depends on all)

---

## Deferred (Medium/Low Gaps — Future Plan)

| Gap | Title | Priority |
|-----|-------|----------|
| GAP-009 | HTTP/2 Multiplexing — Real TCP Integration Test | Medium |
| GAP-010 | Observability — Logging and Metrics | Medium |
| GAP-011 | Dead Code Cleanup — Remove Unused Stages | Medium |
| GAP-012 | HTTP/3 — Stub Engine | Medium |
| GAP-013 | `IHttpClientFactory` Compatibility | Low |
| GAP-014 | Configuration Validation | Low |
| GAP-015 | Dead Configuration — `MaxReconnectAttempts` and `ReconnectInterval` | Low |

---

## Success Criteria

- All 8 tasks have passing `dotnet build` + `dotnet test`
- `ConnectionReuseStage` is live in the Engine pipeline
- TCP drop triggers proper failure notification + backoff
- Per-host connection limits enforced
- `TurboHttpClient` has `IAsyncDisposable`
- Integration tests prove end-to-end `SendAsync()` against Kestrel
- HTTPS works end-to-end
- CLAUDE.md accurately reflects the project state
