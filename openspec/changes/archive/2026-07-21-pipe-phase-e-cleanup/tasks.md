## 1. servus.akka Cleanup — Channel + Watermarks + SendFlushed

- [x] 1.1 `find_references`/`find_callers` on `Channel<WireBuffer>` field, `SendFlushed`, `OnFlushed` in
      `DuplexConnectionBase`/`TcpConnectionStateMachine` to confirm whether QUIC's subclass uses any of
      them (resolves design.md Decision 2)
- [x] 1.2 Remove `Channel<WireBuffer>` outbound queue + send loop from the TCP path (split out of
      `DuplexConnectionBase` if shared with QUIC, per 1.1's findings)
- [x] 1.3 Remove `SendFlushed` event and `OnFlushed` callback wiring from `TcpConnectionStateMachine`
      (and from `DuplexConnectionBase` if confirmed TCP-only)
- [x] 1.4 Remove `_bytesInFlight`, `_highWatermark`, `_lowWatermark` fields and watermark-crossing logic
      from `TcpConnectionStateMachine`
- [x] 1.5 Remove `WireBuffer.Rent` call sites from TCP send/receive paths (class stays, QUIC keeps using
      it)
- [x] 1.6 Update/remove servus.akka unit tests that scripted the channel/watermark/`SendFlushed` paths
      directly

## 2. servus.akka Cleanup — ReadEventState + Legacy ReceiveAsync

- [x] 2.1 `find_references` on `ReadEventState` to confirm all TCP call sites are superseded by
      `PipeReadState`/`PipeFlushState` (Phase C) before deleting
- [x] 2.2 Remove `ReadEventState` class/struct
- [x] 2.3 Remove the old `ReceiveAsync(WireBuffer)` + `PipeTo(ReadCompleted)` loop from
      `TcpConnectionStateMachine` — read pump (Phase A/D) is the sole inbound path
- [x] 2.4 Remove `TransportData` creation/dispatch from `TcpConnectionStage`'s TCP path (network port
      carries lifecycle events only, per Phase D)
- [x] 2.5 Remove `TransportDataFlushed` emission from the TCP transport stage
- [x] 2.6 Update/remove servus.akka transport SM tests referencing the deleted `ReadEventState`/
      `ReceiveAsync`/`TransportData` TCP paths

## 3. GaudiHTTP Cleanup — OnOutbound Data Path (completed by Phase D2)

- [x] 3.1 Stage logic bridge (`BridgeTransportData`, `BridgeFlushCompleted/Failed`) removed in D2
- [x] 3.2 `OnOutbound` in both stage logics simplified to push/queue only — no `TransportData`
      type-check branch
- [x] 3.3 `TransportDataFlushed` handling removed from all TCP SMs and class deleted from
      `ITransportInbound.cs` in D2

## 4. GaudiHTTP Cleanup — Outbound Queue Data Items (completed by Phase D2)

- [x] 4.1 `_outboundQueue` no longer contains `TransportData` items — `PostStop` simplified in D2
- [x] 4.2 Lifecycle command ordering verified by full suite (5994 tests green)
- [x] 4.3 Stage-level and body backpressure tests updated for pipe flush model in D2

## 5. Audit Pass

- [x] 5.1 Run `find_dead_code` across `servus.akka` Transport and GaudiHTTP — no pipe-transport
      dead code found
- [x] 5.2 Confirmed `FakeDuplexConnection.InvokeFlushed` dead (0 refs) — deleted
- [x] 5.3 Updated stale `TransportDataFlushed` comments in `SerialBodyPump` and
      `Http11ClientStateMachine`
- [x] 5.4 No orphaned test helpers found (D2 already cleaned up)
- [x] 5.5 Both projects build clean — 0 errors

## 6. Final Verification

- [x] 6.1 Run full GaudiHTTP unit+stage suite — 5994 tests, 0 failures
- [x] 6.2 Run servus.akka submodule tests — 740 tests, 0 failures
- [x] 6.3 Grep verification: no `BridgeTransportData`, no `BridgeFlushCompleted/Failed`, no
      `TransportDataFlushed` (class or handling), `new TransportIo(` only in `TcpStateMachineBase`
