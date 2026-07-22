## 1. TestPipeTransport (DONE)

- [x] 1.1 Create `TestPipeTransport : IConnectionTransport` in `Servus.Akka.TestKit`
- [x] 1.2 Add `TestPipeTransport` unit tests (9 tests green)
- [x] 1.3 Add `AutoConnectWithTransport()` to `TestConnectionStageBuilder`
- [x] 1.4 Add `Transport` property to `TestConnectionStage`

## 2. Phase A: Deferred Request Encoding

- [x] 2.1 H1.1 Client SM: split `OnRequest` — enqueue + `ConnectTransport` only when `Transport` is null; defer header encoding to `OnTransportConnected` via `_pendingEncode` flag
- [x] 2.2 H1.1 Client SM: `OnTransportConnected` override — if `_pendingEncode`, encode the head of `_inFlightQueue` via `Transport.GetMemory`/`Advance`, call `RequestFlush()`, clear flag
- [x] 2.3 H1.0 Client SM: apply same deferred encoding pattern — `OnRequest` defers when `Transport` is null, `OnTransportConnected` encodes
- [x] 2.4 Verify H1.1 Client SM unit tests pass (update tests that assert immediate encoding in `OnRequest` to account for deferred encoding)
- [x] 2.5 Verify H1.0 Client SM unit tests pass
- [x] 2.6 Run full test suite — all 6008 tests green

## 3. Phase B: Production Fallback Removal

- [x] 3.1 H1.1 Client SM: remove `if (Transport is null)` write-path in `OnRequest` (now dead code after deferred encoding)
- [x] 3.2 H1.1 Client SM: remove `else` branches in `EmitDataFrames` / `EmitOwnedDataFrames`
- [x] 3.3 H1.0 Client SM: remove write-path fallbacks (`WriteRequest`, `EmitDataFrames`, `EmitOwnedDataFrames`)
- [x] 3.4 H1.0 Server SM: remove write-path fallbacks — no-op, already pipe-only
- [x] 3.5 H1.1 Server SM: remove any remaining write-path fallbacks — no-op, already pipe-only
- [x] 3.6 H2 Client SM: remove `else` branch in `EmitToTransport`
- [x] 3.7 H2 Server SM: remove `else` branch in `EmitToTransport`
- [x] 3.8 Remove `TransportData` read-side fallback from all TCP SM `DecodeServerData`/`DecodeClientData` — replace with `InvalidOperationException`
- [x] 3.9 Delete `WireBufferWriter` (`src/GaudiHTTP/Protocol/WireBufferWriter.cs`)
- [x] 3.10 Run full test suite — all tests migrated

## 4. Phase C: Stage-Test Migration

- [x] 4.1 Rewrite `EngineTestBase.RespondToConnect` to use `AutoConnectWithTransport()`
- [x] 4.2 Rewrite `CreateFakeConnection` — `OnOutputReceived` for request-response scripting
- [x] 4.3 Rewrite `CreateScriptedConnection` — pipe transport via `OnOutputReceived`
- [x] 4.4 Rewrite `CreateAccumulatingScriptedConnection`, `CreateScriptedConnectionWithClose`, `CreateProxyConnection` using pipe transport
- [x] 4.5 Rewrite `CreateH2Connection` using pipe transport
- [x] 4.6 Rewrite `SendAsync` / `SendManyAsync` / `DrainOutboundBytes` — use `CapturedOutputBytes` from pipe
- [x] 4.7 Rewrite `SendH2EngineAsync` / `SendH2EngineAsyncMany` — same pattern
- [x] 4.8 Migrate H1 stage-test files
- [x] 4.9 Migrate H2 stage-test files (SM assertion migration: ops.Outbound → transport.WrittenMemory)
- [x] 4.10 Migrate remaining test files (`ProtocolNegotiatingStateMachineSpec`, `TransportIoSpec`, engine specs)
- [x] 4.11 Delete `PushData`/`WaitForDataAsync` extensions from `TestConnectionStageExtensions` (replaced by pipe I/O)
- [x] 4.12 Delete `WireBufferTestExtensions` from `GaudiHTTP.Tests/TestSupport/`

## 5. Verification

- [x] 5.1 Run full test suite — all 6008 tests green
- [x] 5.2 Grep for remaining `TransportData.Rent` in production TCP SM code — zero
- [x] 5.3 Grep for remaining `WireBuffer.Rent` in production TCP SM code — zero (except H3/QUIC + body infrastructure)
- [x] 5.4 Grep for remaining `if (Transport is null)` in TCP SM code — only H1.1 deferred-encode guard (expected)
