## Why

Every TCP state machine (H1.0, H1.1, H2 — client and server) carries a dual-path `if (Transport is { }) ... else ... WireBuffer.Rent` pattern for both reads and writes. The pipe-transport migration landed the `IConnectionTransport` infrastructure and `TcpStateMachineBase`, but left the `WireBuffer`/`Ops.OnOutbound` fallback in place everywhere. This dead-code coexistence doubles every I/O path, adds an unnecessary copy on the write side (encode into WireBuffer → copy into pipe), and forces test infrastructure to stay on the legacy `TransportData`-assertion model instead of testing the real production path. It's time to eliminate the dual-path for TCP protocols and let the pipe be the only path.

## What Changes

- **Shared test transport**: Extract and expand `ScriptableTransport` from `TransportIoSpec.cs` into `GaudiHTTP.Tests.Shared` as a reusable `InMemoryTransport` that captures written bytes and supports scripted reads — replacing `FakeOps.Outbound` assertions for wire data verification.
- **H1.0/H1.1 write-side**: Eliminate `WireBuffer.Rent` + `WireBufferWriter` in server response encoding and client request encoding. Encoders write directly into `TransportBufferWriter` (the pipe). Remove the `EmitWireBuffer` helper and its `else` branch from all four H1 state machines.
- **H1.0/H1.1 read-side**: Remove the `if (Transport is null && data is TransportData)` fallback in `DecodeClientData`/`DecodeServerData`. All data arrives via `TransportIo.ProcessReadResult` → `DecodeData(ReadOnlySequence<byte>)`.
- **H2 write-side**: Give `Http2ClientSessionManager` and `Http2ServerSessionManager` direct `IConnectionTransport` access. Frame serialization writes directly into the pipe buffer instead of renting a `WireBuffer`, serializing, then copying via `EmitWireBuffer`. The `EmitBuffer` delegate is replaced by transport access.
- **H2 read-side**: Remove the `TransportData` fallback in `Http2ClientStateMachine.DecodeServerData` and `Http2ServerStateMachine.DecodeClientData`.
- **Body pump write-side**: `IBodyDrainTarget.EmitDataFrames` and `EmitOwnedDataFrames` — remove the `else` branch. Body data writes directly to the pipe when the transport is present (which it always is during body streaming).
- **Cleanup**: Delete `WireBufferWriter`. Remove `TransportData` from `Ops.OnOutbound` for TCP protocols. `WireBufferTestExtensions` in `GaudiHTTP.Tests` becomes dead code.
- **Not in scope**: H3/QUIC (still uses `WireBuffer`/`MultiplexedData`), the `WireBuffer` class itself (stays in servus.akka for QUIC), Akka Streams port shape (lifecycle events still flow through ports).

## Capabilities

### New Capabilities

- `sm-test-transport`: Shared in-memory `IConnectionTransport` implementation for unit-testing state machines without Akka Streams or WireBuffer. Covers scripted reads, written-bytes capture, flush control, and transport lifecycle simulation.

### Modified Capabilities

- `protocol-state-machine-contract`: Remove the `TransportData fallback for pre-connect` scenario — `DecodeServerData`/`DecodeClientData` no longer handles `TransportData` for TCP protocols. `TransportConnected`/`TransportDisconnected` lifecycle dispatch remains.
- `sm-outbound-write`: Remove the `Pre-connect client SM buffers via OnOutbound` scenario. All TCP outbound writes require a connected transport. The `EmitWireBuffer` helper is eliminated.
- `sm-transport-io`: Tighten `RequestRead is a no-op without a transport` — with the fallback path removed, data cannot arrive at all without a transport. The spec should reflect that `DecodeServerData`/`DecodeClientData` only dispatches lifecycle events for TCP protocols.
- `streams-stages-pipeline`: Codify that TCP stage logic never creates or handles `TransportData` items on either port direction. The `_outboundQueue` no longer carries `TransportData`.

## Impact

- **Production code**: All 6 TCP state machines (H10/H11/H2 × client/server), both H2 session managers, `SerialBodyPump`/`PumpSlot`, `BufferedBodyReader`. `WireBufferWriter` deleted.
- **Test code**: ~100+ test files that construct `WireBuffer.Rent` + `TransportData.Rent` for feeding SMs, or assert on `FakeOps.Outbound` as `TransportData`. These migrate to `InMemoryTransport.Feed()` for reads and `InMemoryTransport.WrittenBytes` for write assertions.
- **Dependencies**: No external dependency changes. `WireBuffer` stays in servus.akka.
- **Risk**: High test churn but mechanically uniform — every test follows the same pattern replacement. The production change is a strict simplification (removing dead branches), not a behavioral change.
