## 1. Extract TcpStateMachineBase

- [x] 1.1 Create `TcpStateMachineBase<TOps>` in `src/GaudiHTTP/Protocol/` with: `TransportIo` field,
      `protected IConnectionTransport? Transport` property, `protected TOps Ops` property,
      lifecycle dispatch methods (`DispatchLifecycleEvent(ITransportInbound)`), `RequestFlush()`,
      and abstract hooks (`DecodeData`, `OnTransportConnected`, `OnTransportDisconnected`,
      `OnFlushCompleted`, `OnTransportLost`, `OnCleanup`)
- [x] 1.2 Move `TransportIo` construction into the base class constructor (accepts `self`, `shouldPause`,
      `decode`, `onFlushCompleted`, `onTransportLost` via constructor params or abstract methods)
- [x] 1.3 Add `bool TryHandleAsyncResult(object msg)` to the base class (wraps `_tio.OnAsyncResult`)
- [x] 1.4 Unit test: `TcpStateMachineBase` lifecycle — `DispatchLifecycleEvent(TransportConnected)` calls
      `OnTransportConnected` and starts read loop; `DispatchLifecycleEvent(TransportDisconnected)` calls
      `OnTransportDisconnected`; `TryHandleAsyncResult` returns true for `ReadCompleted`/`FlushCompleted`
- [x] 1.5 Unit test: `RequestFlush` sync fast-path calls `OnFlushCompleted` immediately;
      async path sets flush-in-progress

## 2. Convert H1.1 Client SM (already partially done)

- [x] 2.1 Make `Http11ClientStateMachine` extend `TcpStateMachineBase<IClientStageOperations>` —
      move `_tio` to base class, implement abstract hooks
- [x] 2.2 Replace `_tio.OnConnected` / `_tio.OnDisconnected` calls in `DecodeServerData` with
      `DispatchLifecycleEvent(data)` from base
- [x] 2.3 Replace `if (_tio.OnAsyncResult(msg)) return;` in `OnBodyMessage` with
      `if (TryHandleAsyncResult(msg)) return;`
- [x] 2.4 Replace `_tio.Cleanup()` in `Cleanup()` with `CleanupTransportIo()`
- [x] 2.5 Verify `_tio.RequestFlush()` calls map to `RequestFlush()` from base class
- [x] 2.6 Run unit+stage suite — green, no regressions (5995 tests, 0 failures)

## 3. Convert H1.0 Client SM

- [x] 3.1 Make `Http10ClientStateMachine` extend `TcpStateMachineBase<IClientStageOperations>`
- [x] 3.2 Replace boilerplate: lifecycle dispatch, `OnAsyncResult`, cleanup
- [x] 3.3 Convert `EmitDataFrames` to dual-path: direct pipe write when `Transport` is not null,
      `WireBuffer` fallback when null (pre-connect)
- [x] 3.4 Convert `EncodeRequest` (in `OnRequest`) to dual-path: direct pipe write when `Transport`
      is not null, `WireBuffer` fallback when null
- [x] 3.5 Remove `TransportDataFlushed` handling from `DecodeServerData` (already absent)
- [x] 3.6 Run unit+stage suite — green (17 tests)

## 4. Convert H1.1 Server SM

- [x] 4.1 Make `Http11ServerStateMachine` extend `TcpStateMachineBase<IServerStageOperations>`
- [x] 4.2 Replace boilerplate: lifecycle dispatch, `OnAsyncResult`, cleanup
- [x] 4.3 Convert `OnResponse` header encoding to direct pipe write via `EmitWireBuffer` helper
- [x] 4.4 Convert `EmitDataFrames` / `EmitOwnedDataFrames` to direct pipe write
- [x] 4.5 Convert remaining encode sites: `EmitBufferedBody`, `EmitChunkedTerminator`,
      `SendInformational`, `TryHandleH2cUpgrade` — all via `EmitWireBuffer` helper
- [x] 4.6 Remove `TransportDataFlushed` handling from `DecodeClientData`
- [x] 4.7 Run unit+stage suite — green (50 tests)

## 5. Convert H1.0 Server SM

- [x] 5.1 Make `Http10ServerStateMachine` extend `TcpStateMachineBase<IServerStageOperations>`
- [x] 5.2 Replace boilerplate: lifecycle dispatch, `OnAsyncResult`, cleanup
- [x] 5.3 Convert `EmitDataFrames` / `EmitOwnedDataFrames` to dual-path direct pipe write
- [x] 5.4 Run unit+stage suite — green (29 tests)

## 6. Convert H2 Client SM

- [x] 6.1 Make `Http2ClientStateMachine` extend `TcpStateMachineBase<IClientStageOperations>`
- [x] 6.2 Replace boilerplate: lifecycle dispatch, `OnAsyncResult`, cleanup
- [x] 6.3 Convert `Http2ClientSessionManager.EmitFrame` via `EmitBuffer` delegate from parent SM
- [x] 6.4 Convert `Http2ClientSessionManager.EmitDataFrames` via `EmitBuffer` delegate
- [x] 6.5 Convert `EncodeRequest` / preface encoding — `TryBuildPreface` returns `WireBuffer?`,
      callers use `EmitWireBuffer`
- [x] 6.6 Remove `TransportDataFlushed` handling (already absent from H2 client)
- [x] 6.7 Run unit+stage suite — green (5995 tests)

## 7. Convert H2 Server SM

- [x] 7.1 Make `Http2ServerStateMachine` extend `TcpStateMachineBase<IServerStageOperations>`
- [x] 7.2 Replace boilerplate: lifecycle dispatch, `OnAsyncResult`, cleanup
- [x] 7.3 Convert `Http2ServerSessionManager.EmitFrame` / `EmitBufferedDataFrames` via `EmitBuffer`
      delegate from parent SM
- [x] 7.4 Run unit+stage suite — green (5995 tests)

## 8. Remove Stage Logic Bridge

- [x] 8.1 Remove `BridgeTransportData` method from `HttpClientConnectionStageLogic`
- [x] 8.2 Remove `BridgeTransportData` method from `HttpServerConnectionStageLogic`
- [x] 8.3 Remove `_transport`, `_flushGen`, `_flushInProgress` fields from both stage logics
- [x] 8.4 Remove `BridgeFlushCompleted` / `BridgeFlushFailed` handling from `OnStageActorMessage`
      in both stage logics
- [x] 8.5 Simplify `OnOutbound` — remove the `TransportData` type-check branch; all items go
      directly to network port push/queue
- [x] 8.6 Simplify `PostStop` — remove `TransportData` disposal from `_outboundQueue` drain
- [x] 8.7 Delete `BridgeFlushCompleted` / `BridgeFlushFailed` from `PipeIoMessages.cs`
- [x] 8.8 Run unit+stage suite — green (5995 tests)

## 9. Dead Code Audit

- [x] 9.1 `find_references` on `TransportDataFlushed` — zero callers, deleted from `ITransportInbound.cs`
- [x] 9.2 Removed dead test references to `TransportDataFlushed` in `Http11ServerBodyBackpressureSpec`
- [x] 9.3 Build clean — zero errors from removals
- [x] 9.4 `TransportIo` only instantiated in `TcpStateMachineBase` — verified via grep

## 10. Final Verification

- [x] 10.1 Run full GaudiHTTP unit+stage suite — 5994 tests, 0 failures
- [x] 10.2 Run servus.akka submodule tests — 6 pre-existing failures (FakeDuplexConnection compat,
      not caused by this change); no new failures
- [x] 10.3 Grep verification: no `BridgeTransportData` anywhere; no standalone `new TransportIo(`
      outside base class; `TransportDataFlushed` fully deleted
