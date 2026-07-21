## Why

TCP state machines keep `if (Transport is null) ... WireBuffer.Rent ... Ops.OnOutbound(TransportData.Rent(...))` fallback paths in production code. These exist because (a) the H1.1 Client SM encodes the request in the same `OnRequest` call that emits `ConnectTransport` — before `TransportConnected` arrives — so it must use the WireBuffer/port fallback, and (b) stage-level tests use `TestConnectionStage` which pushes `TransportData` items through the Akka Streams port.

Eliminating the fallbacks requires three sequential steps:
1. **Deferred encoding**: SMs must not encode requests/responses before `TransportConnected` delivers a transport
2. **Fallback removal**: once encoding is deferred, the `Transport is null` write path is dead and can be deleted
3. **Stage-test migration**: `TestConnectionStage` delivers a real `IConnectionTransport` via `TransportConnected`; stage-tests use pipe I/O instead of `TransportData` items on the port

## What Changes

### Phase A: Deferred Request Encoding (H1.1 Client SM)
- The H1.1 Client SM currently emits `ConnectTransport` AND encodes the request headers in the same `OnRequest` call. This must be split: `OnRequest` enqueues the request and emits `ConnectTransport`, but defers encoding until `OnTransportConnected` fires (when `Transport` becomes non-null). The H1.0 Client SM already has a similar pattern via its `_connectionClosed` flag.
- H2 Client SM already defers encoding via its preface mechanism — no change needed.

### Phase B: Production Fallback Removal
- Remove `if (Transport is null)` write-path fallbacks from all TCP SMs (H1.0/H1.1 Client+Server, H2 Client+Server). All writes go through `Transport.GetMemory`/`Advance`/`FlushAsync`.
- Remove `TransportData` read-side fallback from `DecodeServerData`/`DecodeClientData` — throw `InvalidOperationException` on unexpected `TransportData`.
- Delete `WireBufferWriter`.

### Phase C: Stage-Test Migration
- `TestConnectionStage` delivers `TestPipeTransport` via `AutoConnectWithTransport()` (already implemented in Phase 1).
- `EngineTestBase` helpers rewritten to use pipe I/O instead of `TransportData` items.
- Stage-test files migrated from `PushData`/`WaitForDataAsync` to `transport.FeedInputAsync`/`ReadOutputAsync`.

**Not in scope**: H3/QUIC (stays on `MultiplexedData`), `WireBuffer` class (stays in servus.akka).

## Capabilities

### New Capabilities
- `test-pipe-transport`: `TestPipeTransport` implementation and `TestConnectionStage` integration in `Servus.Akka.TestKit` — **already implemented**.
- `deferred-request-encoding`: H1.1 Client SM defers request encoding until `TransportConnected` delivers a non-null transport.

### Modified Capabilities
- `protocol-state-machine-contract`: `OnRequest` no longer encodes immediately; encoding happens in `OnTransportConnected` callback. `TransportData` fallback removed.
- `sm-outbound-write`: All TCP outbound writes require a connected transport. No `if (Transport is null)` guard.
- `streams-stages-pipeline`: Stage-tests use pipe transport; `TestConnectionStage` delivers real `IConnectionTransport`.

## Impact

- **H1.1 Client SM**: behavioral change (deferred encoding) — request is buffered until transport connects
- **All TCP SMs**: write-path simplification (fallback removal)
- **servus.akka TestKit**: `TestPipeTransport` (done), `AutoConnectWithTransport()` (done)
- **EngineTestBase + stage-tests**: full rewrite of data flow (Phase C)
- **Risk**: deferred encoding changes the timing of when encoded bytes appear on the wire — tests that assert on immediate encoding need adjustment
