## 1. Stage Operations Interface Changes

- [x] 1.1 Add `IActorRef Self { get; }` to `IClientStageOperations`
- [x] 1.2 Add `IActorRef Self { get; }` to `IServerStageOperations`
- [x] 1.3 Implement `Self` in `HttpClientConnectionStageLogic` (returns `StageActor.Ref`)
- [x] 1.4 Implement `Self` in `HttpServerConnectionStageLogic` (returns `StageActor.Ref`)
- [x] 1.5 Extend the client/server operations mock/test-double used by SM unit tests with a settable
      `Self` (backed by a `TestProbe`), so Phase C tests can assert on `PipeTo` traffic

## 2. Message Types + Cached Delegates

- [x] 2.1 Define `ReadCompleted(ReadResult Result, int Gen)` and `ReadFailed(Exception Ex, int Gen)`
      internal message types
- [x] 2.2 Define `FlushCompleted(FlushResult Result, int Gen)` and `FlushFailed(Exception Ex, int Gen)`
      internal message types
- [x] 2.3 Implement `PipeReadState` — holds the `Func<ReadResult, object>`/`Func<Exception, object>`
      transform delegates for `PipeTo`, scoped to a transport generation
- [x] 2.4 Implement `PipeFlushState` — same shape as `PipeReadState` for `FlushResult`
- [x] 2.5 Unit test: `PipeReadState`/`PipeFlushState` produce correctly-generationed messages without
      per-call allocation (delegate identity stable across calls within the same generation)

## 3. SM Transport I/O Base Logic

- [x] 3.1 Add `_transport` (`IConnectionTransport?`), `_transportGen`, `_readInProgress`,
      `_flushInProgress`, `_syncReadBudget`, `_readState`, `_flushState` fields — shared base class or
      identical per-SM shape (decide during implementation; either is acceptable per design.md Decision 1)
- [x] 3.2 Implement `RequestRead()`: no-op if `_transport == null` or `ShouldPauseNetwork`; else sync
      fast-path (budget-gated) + `PipeTo` fallback
- [x] 3.3 Implement `OnReadCompleted(ReadResult)`: calls abstract `DecodeData(ReadOnlySequence<byte>)`,
      then `_transport.AdvanceTo(consumed, examined)`, then `RequestRead()`
- [x] 3.4 Implement `RequestFlush()`: no-op if `_transport == null`; else sync fast-path + `PipeTo`
      fallback, setting `_flushInProgress`
- [x] 3.5 Implement `OnFlushCompleted(FlushResult)`: clears `_flushInProgress`, signals capacity available
      to whichever body pump is active; treats `IsCompleted = true` as connection loss
- [x] 3.6 Implement `OnAsyncResult(object msg)` dispatch: `ReadCompleted`/`ReadFailed` →
      `OnReadCompleted`/failure handling; `FlushCompleted`/`FlushFailed` → `OnFlushCompleted`/failure
      handling; generation mismatch → drop; anything else → `OnBodyMessage(msg)`
- [x] 3.7 Extend `OnTransportEvent`/`DecodeServerData`/`DecodeClientData` dispatch: `TransportConnected`
      increments `_transportGen`, resets `_readInProgress`/`_flushInProgress`; if `Transport` is non-null,
      store it, create new `PipeReadState`/`PipeFlushState`, call `RequestRead()`; `TransportDisconnected`
      sets `_transport = null` and increments `_transportGen`
- [x] 3.8 Unit test: sync fast-path processes multiple reads without `PipeTo` (mock transport with
      pre-buffered `ReadResult`s)
- [x] 3.9 Unit test: async path bridges via `PipeTo`, generation guard drops stale
      `ReadCompleted`/`FlushCompleted` after a simulated reconnect
- [x] 3.10 Unit test: `ShouldPauseNetwork` prevents `RequestRead`; `BodyResumed`/reader-drain re-arms it
- [x] 3.11 Unit test: `RequestRead()`/`RequestFlush()` are no-ops when `_transport` is null (legacy mode
      untouched)
- [x] 3.12 Unit test: `_transportGen` increments on every `TransportConnected`, transport present or not

## 4. Encoder Migration to IBufferWriter<byte>

- [x] 4.1 Refactor `Http10ClientEncoder` to write against `IBufferWriter<byte>`
- [x] 4.2 Refactor `Http11ClientEncoder` to write against `IBufferWriter<byte>`
- [x] 4.3 Refactor `Http11ServerEncoder` to write against `IBufferWriter<byte>`
- [x] 4.4 Refactor `Http2ClientEncoder` to write against `IBufferWriter<byte>`
- [x] 4.5 Refactor `Http2ServerEncoder` to write against `IBufferWriter<byte>`
- [x] 4.6 Implement `WireBufferWriter : IBufferWriter<byte>` adapter wrapping a rented `WireBuffer`, for
      the legacy call site
- [x] 4.7 Update SM legacy call sites to encode via the `WireBufferWriter` adapter, then wrap the written
      span into `TransportData` for `ops.OnOutbound` (behavior-preserving)
- [x] 4.8 Update encoder unit tests to cover `IBufferWriter<byte>` output (e.g., `ArrayBufferWriter<byte>`
      as test double) alongside existing `WireBuffer`-via-adapter coverage
- [x] 4.9 Roslyn Navigator check: confirm no remaining `SpanWriter`/`WireBuffer` call sites inside the five
      encoders themselves (adapter usage is at the SM call site only)

## 5. SM Integration — Dual-Path (per protocol, TCP only)

- [x] 5.1 H1.0 client SM: branch `DecodeServerData`/`OnRequest`/`OnBodyMessage` on `_transport != null`
      per protocol-state-machine-contract deltas; pipe branch uses `DecodeData`/encoder-via-transport/
      `RequestFlush`; legacy branch unchanged
- [x] 5.2 H1.1 client SM: same branch pattern, plus `SerialBodyPump` chunk delivery fork
      (`_flushInProgress` gate vs. existing credit) per flow-control delta
- [x] 5.3 H1.1 server SM: same pattern as H1.1 client, server-side (`DecodeClientData`/`OnResponse`)
- [x] 5.4 H1.0 server SM: same pattern as H1.0 client, server-side
- [x] 5.5 H2 client SM: `DecodeData(ReadOnlySequence)` for frame decode in pipe mode; encoder writes to
      `_transport`; `FlowControlledBodyPump` write-call-site fork (WINDOW_UPDATE gating untouched)
- [x] 5.6 H2 server SM: same pattern as H2 client, server-side
- [x] 5.7 Update SM unit tests: existing tests continue exercising the legacy path unmodified; add
      parallel pipe-mode tests per SM using a mock `IConnectionTransport` (construct with
      `TransportConnected(Transport: mock)`, assert `DecodeData`/encoder/`RequestFlush` calls)
- [x] 5.8 Confirm H3 client/server SMs are untouched (no `_transport` field, no dual-path branch) — grep
      diff to verify scope discipline

## 6. Body Pump Adaptation

- [x] 6.1 `SerialBodyPump`: add pipe-mode chunk-delivery gate on `_flushInProgress`, leaving
      `_availableBytes`/`maxBytes`/`ResetCredit` untouched for the legacy branch
- [x] 6.2 `FlowControlledBodyPump`: fork only the write call site (transport vs. `ops.OnOutbound`); leave
      `Reserve`/`Refund`/`OnWindowUpdate`/`_windowBlockedStreams` untouched
- [x] 6.3 Unit test: `SerialBodyPump` in pipe mode — chunk withheld while `_flushInProgress`, delivered
      once `OnFlushCompleted` clears it
- [x] 6.4 Unit test: `FlowControlledBodyPump` write-call-site fork — same window-gating decisions produce
      a transport write in pipe mode and a `TransportData` emission in legacy mode for otherwise identical
      inputs
- [x] 6.5 Regression: existing `SerialBodyPump`/`FlowControlledBodyPump` credit/window unit test suites
      pass unmodified (legacy path byte-for-byte unchanged)

## 7. Verification

- [x] 7.1 Run full unit+stage suite (`GaudiHTTP.Tests`, ~5660) — target all green, no legacy-path
      regressions
- [x] 7.2 Run integration test suites (Client, End2End, Server) — target all green; these exercise only
      the legacy path since the transport stage isn't switched yet, so they validate this phase caused no
      behavioral drift
- [x] 7.3 Roslyn Navigator: `get_diagnostics` clean on all touched projects; `find_references` sanity
      check that `TransportData`/`ops.OnOutbound` legacy call sites are unchanged in signature
- [x] 7.4 Confirm scope discipline: no changes to `TcpConnectionStage`/`TcpConnectionStateMachine`
      (servus.akka), no `HttpClientConnectionStageLogic`/`HttpServerConnectionStageLogic` thinning beyond
      `Self`, no H3/QUIC files touched
