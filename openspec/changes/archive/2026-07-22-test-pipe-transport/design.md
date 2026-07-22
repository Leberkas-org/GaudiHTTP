## Context

The pipe-transport migration introduced `TcpStateMachineBase` and `TransportIo` for TCP protocols. SMs read via `ReadAsync`/`AdvanceTo` and write via `GetMemory`/`Advance`/`FlushAsync` — all through an `IConnectionTransport` delivered by `TransportConnected`. However, the H1.1 Client SM encodes request headers in `OnRequest` BEFORE `TransportConnected` arrives (the connect is lazy — triggered by the first request). This forces a `if (Transport is null)` WireBuffer/OnOutbound fallback in all TCP SMs.

Stage-level tests use `TestConnectionStage` which intercepts `TransportData` items on the Akka Streams port to script request/response exchanges. With pipe transport, data flows through the pipe, not the port — so stage-tests can't intercept it the same way. Migrating stage-tests requires that SMs don't emit `TransportData` at all, which requires deferred encoding.

## Goals / Non-Goals

**Goals:**
- H1.1 Client SM defers encoding until transport is connected
- All `if (Transport is null)` fallback branches removed from TCP SM production code
- Stage-tests deliver `TestPipeTransport` and use pipe I/O
- Zero `TransportData` items on the Akka Streams port for TCP protocols

**Non-Goals:**
- H3/QUIC changes
- Changing `TransportIo` or `TcpStateMachineBase` internals
- Backpressure simulation in tests (real pipes handle it naturally)

## Decisions

### Decision 1: Deferred encoding via pending-request buffer

The H1.1 Client SM buffers the request in `OnRequest` and defers encoding to `OnTransportConnected`. On first request, the sequence becomes:

```
OnRequest(request):
  1. Enqueue request in _inFlightQueue
  2. If no endpoint yet: set endpoint, emit ConnectTransport
  3. If Transport is not null: encode immediately (reconnect case)
  4. If Transport is null: mark _pendingEncode = true (first connect)

OnTransportConnected(info):
  1. Store transport (via base class)
  2. If _pendingEncode: encode the head of _inFlightQueue now
  3. Clear _pendingEncode
```

This preserves the lazy-connect pattern while ensuring `Transport` is always non-null when encoding runs.

**H1.0 Client**: already has `_connectionClosed` flag that prevents encoding before connect — needs the same deferred-encode treatment.

**H2 Client**: already defers via `TryEmitPreface` + `EncodeRequest` after `TransportConnected` — no change needed.

**Server SMs**: don't have this problem (transport is always connected before requests arrive).

### Decision 2: Three-phase execution order

**Phase A** (deferred encoding) must come first because it's a behavioral change to the SM that makes the fallback truly dead code. **Phase B** (fallback removal) is safe only after Phase A. **Phase C** (stage-test migration) is safe only after Phase B because stage-tests currently rely on `TransportData` items flowing through the port.

### Decision 3: TestPipeTransport is ready (Phase 1 done)

`TestPipeTransport` and `AutoConnectWithTransport()` are already implemented and tested. Phase C uses them directly.

### Decision 4: Stage-test response scripting via pipe async loop

`TestPipeTransport.OnOutputReceived(Func<byte[], byte[]?>)` spawns an async loop that reads SM output from the pipe and feeds responses back. For `EngineTestBase` helpers, this replaces the `PushResponse` / `OnOutbound<TransportData>` pattern. `CapturedOutputBytes` (captured at `Advance` time, synchronous) replaces `ReceivedOutbound.OfType<TransportData>()` for request assertions.

## Risks / Trade-offs

**[Risk: Deferred encoding changes wire timing]** → Requests that were encoded synchronously in `OnRequest` now encode in `OnTransportConnected` (one actor message later). Mitigation: the wire bytes are identical; only the timing changes. Tests that assert on immediate `ops.Outbound` after `OnRequest` need to account for the deferred encoding.

**[Risk: Stage-test async complexity]** → `OnOutputReceived` is async (background task). Tests that were synchronous may need `await`. Mitigation: `FeedInputAsync` is properly async; `CapturedOutputBytes` is sync (captured at `Advance` time).

## Open Questions

None — the three-phase ordering is determined by the dependency chain.
