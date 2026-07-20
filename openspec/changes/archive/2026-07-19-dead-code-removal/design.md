## Context

Dead code identified via 3-way analysis: Roslyn `find_dead_code` MCP tool, grep-based reference counting across `src/`, and file-level inspection of recently-refactored areas. All findings verified with zero-reference confirmation.

## Goals / Non-Goals

**Goals:**
- Remove all confirmed dead production code (zero callers outside tests)
- Remove associated test files for deleted classes
- Fix stale naming/comments

**Non-Goals:**
- Remove test-only helpers that are intentional test infrastructure (e.g., `Http3ClientEncoder.EncodeToQpackBlock()` which is documented as "Used by tests")
- Remove protocol-mandated enum members (e.g., `ErrorCode` members defined by RFC 9114 §8.1)
- Remove `HasRemainder`, `Settings.Deserialize()`, `QuicVarInt.Decode()` or other test-accessible methods — these serve legitimate test verification

## Decisions

### D1: Delete entire dead classes + their test files

`StreamTracker`, `ChunkExtensionParser`, `ConnectionReuseEvaluator`, `ConnectionReuseDecision` — delete both production and test files. The test files only test dead code.

### D2: Dead production methods removed, not moved

Methods like `GetResponsePipeReader`, `ReturnBuffer`, `FailInflightRequest` are removed entirely. No deprecation — they're internal and have zero callers.

### D3: ConnectionState push infrastructure removed

`RecordPush`, `IsPushCancelled`, `MaxPushId` — the push infrastructure is dead because the client rejects pushes directly via `CancelPushFrame` + `ResetStream` without tracking them. If push support is ever needed, it will be redesigned.

### D4: File rename via git mv

`ClientCorrelationKeys.cs` → `OptionsKey.cs` to match the class name it contains. Done via `git mv` to preserve history.

## Risks / Trade-offs

- **[ReturnBuffer removal]** — Could be a latent bug (buffers never returned to pool). Removing the method doesn't make it worse — if the pool was intended to recycle, the bug existed before. The `RentBuffer` path uses `[ThreadStatic]` scratch so the "pool" is actually a per-thread cache. Removing `ReturnBuffer` is safe.
- **[Test file removal]** — Tests for dead code are dead tests. Removing them reduces noise in test runs.
