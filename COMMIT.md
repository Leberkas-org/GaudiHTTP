## TASK-026-008: Validation + documentation

### Changes

**ConnectionStage** (`src/TurboHttp/Transport/ConnectionStage.cs`):
- Added `_connectionGen` generation counter to prevent stale inbound pump completions from destroying new connection state
- `_onInboundComplete` callback now carries `(TlsCloseKind, int Gen)` tuple; stale generations are ignored
- Added `_pendingReads.Clear()` in `_onLeaseAcquired` after generation increment to discard stale DataItem/CloseSignalItem from prior connection's pump

**Http10DecoderStage** (`src/TurboHttp/Streams/Stages/Decoding/Http10DecoderStage.cs`):
- Added `CloseSignalItem` handling: distinguishes CleanClose (triggers TryDecodeEof) from AbruptClose (FailStage with HttpRequestException)

### Test Results

| Suite | Result |
|-------|--------|
| Unit Tests | 3492/3492 pass |
| Stream Tests | 808/808 pass |
| H10 Integration | 75/77 stable (Error-H10-003 pre-existing isolation issue, 1 random flake/run) |
| H11 Integration | 100% green |
| H2 Integration | 100% green |
| Smoke + TLS | 100% green |

### Known Issues

- **Error-H10-003**: "Mid-response connection abort raises exception" — passes in isolation (10s), fails in full suite due to shared actor system contention. Pre-existing, not a regression.
- **Random H10 timeout flakes**: 1 per run, different test each time. Shared Kestrel fixture + actor system contention under concurrent test execution.
