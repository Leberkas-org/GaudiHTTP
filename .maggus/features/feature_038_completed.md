<!-- maggus-id: c8e2153f-8a3d-4426-9fe3-e1772768458a -->
# Feature 038: Fix BidiFlow Feedback Race + Http1XCorrelation Back-Pressure

## Introduction

Two correctness bugs in the Streams layer that both cause request-ordering violations under certain conditions.

**Bug A — `RedirectBidiStage` `_inFlightCount` leak:** Every redirect hop inflates `_inFlightCount` by 1 because the redirect path in `onPush(InResponse)` does not decrement the counter for the consumed original request, while `TryEmitRedirect` still increments it for the new redirect request. The result is that `TryCompleteIfDone` Case 2 (all in-flight resolved) never fires — the stream can only complete via Case 1 (InResponse upstream closed). In production, where the response inlet is long-lived, this risks the feature BidiFlow chain not completing cleanly after a redirect chain finishes. No `_retryTransactionActive`-style guard exists to protect the intermediate state.

**Bug C — `Http1XCorrelationStage` back-pressure violation:** `_pipelineUnlocked` is permanently set to `true` after the first request-response pair, allowing unlimited requests to queue in `_pending` without waiting for responses. The `InReset` inlet intended to reset this flag is wired to `Source.Empty<NotUsed>()` in both engines and never fires. This violates RFC 9112 §9 (strictly ordered request-response pairs on a single HTTP/1.x connection).

### Architecture Context

- **Vision alignment:** Correctness is a prerequisite for performance and reliability.
- **Components involved:** `Streams/Stages/Features/RedirectBidiStage.cs`, `Streams/Stages/Routing/Http1XCorrelationStage.cs`, `Streams/Http10Engine.cs`, `Streams/Http11Engine.cs`, `StreamTests/Concurrency/BidiFlowFeedbackRaceTests.cs` (already written, untracked).
- **No new components introduced** — pure correctness fixes within existing stages.
- **Architecture boundary respected:** No changes to Protocol, Transport, or Client layers.

---

## Goals

- `RedirectBidiStage._inFlightCount` stays stable across redirect hops — Case 2 completion works correctly.
- `RetryBidiStage` and `RedirectBidiStage` share the same transaction-guard pattern for in-flight tracking.
- `Http1XCorrelationStage` enforces strict one-request-in-flight semantics per RFC 9112 §9.
- `InReset` inlet and all associated dead code removed from `Http1XCorrelationStage`, `Http10Engine`, `Http11Engine`.
- `BidiFlowFeedbackRaceTests.cs` (already written) passes and is committed as regression coverage for Bug A.
- Four new back-pressure tests for Bug C pass deterministically.

---

## Tasks

### TASK-038-001: Fix RedirectBidiStage — Transaction Guard + _inFlightCount Decrement

**Description:** As the HTTP runtime, I want `RedirectBidiStage` to manage `_inFlightCount` correctly across redirect hops so that `TryCompleteIfDone` Case 2 fires reliably and the stage matches the proven `RetryBidiStage` pattern.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-038-005
**Parallel:** yes — can run alongside TASK-038-002

**Acceptance Criteria:**
- [x] New field `_redirectTransactionActive` (bool) added to `Logic`.
- [x] Redirect path in `onPush(InResponse)` updated to:
  ```csharp
  _redirectTransactionActive = true;
  _readyRedirects.Enqueue(newRequest);
  TryEmitRedirect();
  _inFlightCount--;
  TryPullResponse();
  _redirectTransactionActive = false;
  TryCompleteIfDone();
  ```
- [x] `TryCompleteIfDone()` has `if (_redirectTransactionActive) return;` as its first statement.
- [x] Same guard applied in the `ProtocolDowngrade` and `MaxRedirects` catch blocks (both already call `TryCompleteIfDone` via the non-redirect path — verify no guard needed there, i.e., `_redirectTransactionActive` is only ever `true` in the redirect sub-path).
- [x] `BidiFlowFeedbackRaceTests.cs` (untracked file at `src/TurboHttp.StreamTests/Concurrency/BidiFlowFeedbackRaceTests.cs`) is included in the `TurboHttp.StreamTests` project and all 4 tests pass:
  - `BidiLoop-001` — 200 concurrent redirect instances all complete
  - `BidiLoop-002` — retry storm bounded by MaxRetries
  - `BidiLoop-003` — In1 closed during retry cycle
  - `BidiLoop-004` — redirect + slow downstream liveness
- [x] `Roslyn MCP get_diagnostics` on `RedirectBidiStage.cs` returns zero errors.
- [x] No changes to `RetryBidiStage.cs`, `CacheBidiStage.cs`, or any other stage.

---

### TASK-038-002: Rewrite Http1XCorrelationStage — Strict One-Request-In-Flight

**Description:** As the HTTP runtime, I want `Http1XCorrelationStage` to enforce strict HTTP/1.x back-pressure so that only one request is in-flight at a time on a connection, matching RFC 9112 §9.

**Token Estimate:** ~80k tokens
**Predecessors:** none
**Successors:** TASK-038-003, TASK-038-004
**Parallel:** yes — can run alongside TASK-038-001

**Acceptance Criteria:**
- [x] `Http1XCorrelationShape` loses the `InReset` inlet: shape becomes 2 inlets (`InRequest`, `InResponse`) + 2 outlets (`OutResponse`, `OutControl`).
- [x] `_inReset` field removed from `Http1XCorrelationStage`.
- [x] Internal `Logic` state simplified to:
  - `_inFlightRequest`: `HttpRequestMessage?` — the one pending request, or `null`.
  - `_requestUpstreamFinished`, `_responseUpstreamFinished`: completion booleans (unchanged if present).
- [x] `_pending` queue, `_waiting` queue, and `_pipelineUnlocked` flag are all removed.
- [x] Back-pressure contract enforced:
  - A new request is pulled from `InRequest` only when `_inFlightRequest == null`.
  - When a request arrives on `InRequest`, it is stored in `_inFlightRequest` and `StreamAcquireItem` is emitted on `OutControl`. The stage does NOT pull another request.
  - When a response arrives on `InResponse`, it is matched to `_inFlightRequest`, pushed on `OutResponse`, and `_inFlightRequest` is set to `null`. Only then is the next request pulled.
- [x] `PreStart()` removed (no longer needs to pull `_inReset`).
- [x] `onPull(OutResponse)` pulls `InRequest` only if `_inFlightRequest == null` (InResponse is pulled from `onPush(InRequest)` after the request is stored, ensuring serial ordering).
- [x] Completion logic: stage completes only when both upstreams finish and `_inFlightRequest == null`.
- [x] `Roslyn MCP get_diagnostics` on `Http1XCorrelationStage.cs` returns zero errors.
- [x] Existing stream tests that cover correlation still pass (run `dotnet test --project TurboHttp.StreamTests/TurboHttp.StreamTests.csproj`).

---

### TASK-038-003: Remove Dead InReset Wiring from Http10Engine and Http11Engine

**Description:** As a developer, I want the dead `Source.Empty<NotUsed>()` InReset wiring removed from both engine graphs so that no misleading dead code remains.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-038-002
**Successors:** TASK-038-004
**Parallel:** no
**Model:** haiku

**Acceptance Criteria:**
- [x] `Http10Engine.cs`: `var resetSrc = b.Add(Source.Empty<NotUsed>());` and `b.From(resetSrc).To(correlation.InReset);` removed.
- [x] `Http11Engine.cs`: same two lines removed.
- [x] `Grep` for `InReset`, `_inReset` across the solution returns zero results.
- [x] `dotnet build --configuration Release ./src/TurboHttp.sln` succeeds with zero errors.

---

### TASK-038-004: Add Http1XCorrelation Back-Pressure Stream Tests

**Description:** As a developer, I want deterministic stream tests that verify the one-request-at-a-time back-pressure contract so that regressions are caught in CI.

**Token Estimate:** ~70k tokens
**Predecessors:** TASK-038-002, TASK-038-003
**Successors:** TASK-038-005
**Parallel:** no

**Acceptance Criteria:**
- [x] Test file: `TurboHttp.StreamTests/RFC9112/08_Http1XCorrelationBackPressureTests.cs`.
- [x] **Test `bp-001` — Serial ordering:** Send 3 requests sequentially. Verify each response is delivered in FIFO order and no second request is pushed to the encoder before the first response arrives.
- [x] **Test `bp-002` — Back-pressure gate:** Provide 2 requests simultaneously at `InRequest`. Verify only the first request flows to `OutControl` (`StreamAcquireItem` emitted once). The second request is NOT pulled until the first response is delivered on `InResponse`.
- [x] **Test `bp-003` — Upstream completion mid-flight:** Send 1 request, complete `InRequest` upstream before response arrives. Verify stage does not complete prematurely; stage completes only after the response is forwarded.
- [x] **Test `bp-004` — Response without in-flight request:** Regression guard — verify defined behavior (error or ignore) when a response arrives with `_inFlightRequest == null`.
- [x] Each test uses `DisplayName("RFC9112-correlation-bp-NNN: ...")` and `[Fact(Timeout = 5000)]`.
- [x] No `Thread.Sleep`. Akka.Streams test probes used for all synchronization.
- [x] All 4 tests green on a clean run.

---

### TASK-038-005: Verification Gate

**Description:** As the build system, I want a full clean build and test run to confirm feature 038 leaves the codebase green.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-038-001, TASK-038-003, TASK-038-004
**Successors:** none
**Parallel:** no
**Model:** haiku

**Acceptance Criteria:**
- [x] `dotnet build --configuration Release ./src/TurboHttp.sln` — zero errors, zero new warnings.
  - ✓ **VERIFIED** (2026-04-01 02:17): 0 errors, 0 warnings. Build time: 11s
- [x] `dotnet test --project TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` — all tests pass.
  - ✓ **VERIFIED** (2026-04-01 02:18): 835 passed, 0 failed. Duration: 7s 641ms
- [x] `dotnet test --project TurboHttp.Tests/TurboHttp.Tests.csproj` — all tests pass.
  - ✓ **VERIFIED** (2026-04-01 02:19): 3652 passed, 0 failed. Duration: 9s 799ms
- [x] `dotnet test --project TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj` — green (pre-existing failures only, documented).
  - ✓ **VERIFIED** (2026-04-01 03:20): 471 passed, 1 skipped, 0 failed. Duration: 13m 46s. No regressions from feature 038 changes.
- [x] `Grep` for `_pipelineUnlocked`, `_inReset`, `InReset`, `_pending` in `Http1XCorrelationStage.cs` returns zero results.
  - ✓ **VERIFIED** (2026-04-01 02:20): Zero matches found. Dead code fully removed.
- [x] `Grep` for `Source.Empty<NotUsed>()` in `Http10Engine.cs` and `Http11Engine.cs` returns zero results (or only unrelated usages).
  - ✓ **VERIFIED** (2026-04-01 02:20): Zero matches found in engine files. InReset wiring completely removed.
- [x] `BidiLoop-001` through `BidiLoop-004` pass in `BidiFlowFeedbackRaceTests`.
  - ✓ **VERIFIED** (2026-04-01 02:18): 4 tests with correct DisplayName format pass (included in 835 passed stream tests). Namespace: `TurboHttp.StreamTests.Concurrency`.
- [x] `bp-001` through `bp-004` pass in `Http1XCorrelationBackPressureTests`.
  - ✓ **VERIFIED** (2026-04-01 02:18): 4 tests with correct DisplayName format pass (included in 835 passed stream tests). File: `RFC9112/08_Http1XCorrelationBackPressureTests.cs`.

---

## Task Dependency Graph

```
TASK-038-001 ──────────────────────────────────────────────┐
                                                            ▼
TASK-038-002 ──→ TASK-038-003 ──→ TASK-038-004 ──→ TASK-038-005
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-038-001 | ~40k | none | yes (with 002) | — |
| TASK-038-002 | ~80k | none | yes (with 001) | — |
| TASK-038-003 | ~20k | 002 | no | haiku |
| TASK-038-004 | ~70k | 002, 003 | no | — |
| TASK-038-005 | ~15k | 001, 003, 004 | no | haiku |

**Total estimated tokens:** ~225k

---

## Functional Requirements

- **FR-1:** `RedirectBidiStage._inFlightCount` MUST be decremented exactly once per consumed response (including redirect responses). A redirect response consumed internally must decrement the counter before `TryEmitRedirect` increments it for the outgoing redirect request.
- **FR-2:** `RedirectBidiStage.TryCompleteIfDone()` MUST return early when `_redirectTransactionActive == true`, matching the `RetryBidiStage._retryTransactionActive` pattern.
- **FR-3:** `Http1XCorrelationStage` MUST NOT pull a second request from `InRequest` until the first response has been pushed on `OutResponse`.
- **FR-4:** `Http1XCorrelationStage` shape MUST have exactly 2 inlets and 2 outlets after the fix. `InReset` is removed.
- **FR-5:** `Http1XCorrelationStage` MUST emit exactly one `StreamAcquireItem` on `OutControl` per in-flight request.
- **FR-6:** Stage completion: `Http1XCorrelationStage` MAY complete only when both `InRequest` and `InResponse` are finished AND `_inFlightRequest == null`.
- **FR-7:** No mutable queue collections (`Queue<T>`) remain in `Http1XCorrelationStage.Logic`.

---

## Non-Goals

- No HTTP/1.1 pipelining support — strict serial order is the correct default.
- No changes to `ConnectionReuseStage`, `ExtractOptionsStage`, or `ConnectionStage`.
- No changes to `RetryBidiStage`, `CacheBidiStage`, or any other feature stage.
- No Protocol or Transport layer changes.
- No performance benchmarks — these are correctness fixes.
- No changes to HTTP/2 or HTTP/3 correlation stages.

---

## Technical Considerations

- **Akka.Streams single-threaded-per-stage guarantee:** No synchronization primitives needed inside stage logic — `OnPush`/`OnPull` handlers run sequentially.
- **`_redirectTransactionActive` scope:** The guard only needs to be active during the redirect sub-path of `onPush(InResponse)`. The `ProtocolDowngrade` and `MaxRedirects` catch blocks follow the non-redirect (pass-through) path and do not need the guard.
- **`StreamAcquireItem` emission:** With the new model, every request arrival emits one `StreamAcquireItem` (one in-flight at a time). Verify this does not break `ConnectionStage` handling — the stage already handles one `StreamAcquireItem` per request, so this is unchanged behavior.
- **`BidiFlowFeedbackRaceTests.cs`:** Already fully written and untracked at `src/TurboHttp.StreamTests/Concurrency/BidiFlowFeedbackRaceTests.cs`. TASK-038-001 must include it in the project (add to `.csproj` if needed or verify it is auto-included via glob).

**Critical files:**
- `src/TurboHttp/Streams/Stages/Features/RedirectBidiStage.cs` — Bug A fix
- `src/TurboHttp/Streams/Stages/Routing/Http1XCorrelationStage.cs` — Bug C rewrite
- `src/TurboHttp/Streams/Http10Engine.cs` — remove 2 lines
- `src/TurboHttp/Streams/Http11Engine.cs` — remove 2 lines
- `src/TurboHttp.StreamTests/Concurrency/BidiFlowFeedbackRaceTests.cs` — commit (already written)
- `src/TurboHttp.StreamTests/RFC9112/08_Http1XCorrelationBackPressureTests.cs` — new file

---

## Success Metrics

- Zero `_redirectTransactionActive`-unguarded `TryCompleteIfDone` calls in the redirect sub-path.
- Zero `_pipelineUnlocked` / `InReset` / queue references in `Http1XCorrelationStage.cs` after the fix.
- All 4 `BidiLoop-*` and all 4 `bp-*` tests pass deterministically on 10 consecutive runs.
- Full build green with zero new compiler warnings.

---

## Open Questions

*(none — all resolved during brainstorming)*
