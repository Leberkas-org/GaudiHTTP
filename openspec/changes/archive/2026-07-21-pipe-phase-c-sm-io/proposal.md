## Why

`pipe-transport-tcp` (master plan, `openspec/changes/pipe-transport-tcp/`) moves transport I/O ownership
for TCP protocols from the Akka Streams stage into the protocol state machine, reading/writing
`IConnectionTransport` (a `PipeReader`/`PipeWriter` pair, delivered by Phase A) directly instead of
pushing/pulling `TransportData` through the stage port. Phase B made `FrameDecoder` accept
`ReadOnlySequence<byte>` so decode can work directly against pipe-delivered data. Phase C is the third
step: give the state machines (and their operations interfaces, encoders, and body pumps) the ability to
drive `IConnectionTransport` themselves, **without** yet switching the transport stage to pipe mode. The
transport stage still sends `TransportData` through the port until Phase D flips it over — so every SM
in this phase must support both the new pipe path and the existing port path side by side.

Doing the SM-side work as its own phase, ahead of the stage switch, lets the read/write loop, generation
guards, and encoder refactor be built and unit-tested against a mock `IConnectionTransport` in isolation,
without simultaneously changing how the stage and transport interact.

## What Changes

- **GaudiHTTP Stage Operations**: Add `IActorRef Self { get; }` to `IClientStageOperations` and
  `IServerStageOperations`, implemented by `HttpClientConnectionStageLogic` /
  `HttpServerConnectionStageLogic` as the StageActor's ref. This is the SM's sole handle for bridging
  async transport results back to the actor thread via `PipeTo`.
- **GaudiHTTP Protocol State Machines**: Add transport I/O ownership — `_transport`
  (`IConnectionTransport?`), `_transportGen`, `_readInProgress`, `_flushInProgress`, `_syncReadBudget` —
  plus `RequestRead()` / `OnReadCompleted(ReadResult)` / `RequestFlush()` / `OnFlushCompleted(FlushResult)`
  / `OnAsyncResult(object)` / `OnTransportEvent(ITransportInbound)`. Shared across H1.0, H1.1, H2 client
  and server SMs (either via a common base or per-SM with identical shape — implementation detail left to
  Phase C tasks).
- **GaudiHTTP Protocol State Machines — dual-path**: Every SM entry point that currently reads
  (`DecodeServerData`/`DecodeClientData`) or writes (`ops.OnOutbound(TransportData)`) MUST branch on
  `_transport != null`. Non-null (pipe mode, exercised only by tests in this phase — nothing wires it up
  yet): use `ReadAsync`/`AdvanceTo`/`GetMemory`/`Advance`/`FlushAsync`. Null (legacy, what production
  traffic still uses after this phase): unchanged existing behavior. This dual-path is temporary
  scaffolding, removed in Phase D when the transport stage starts always supplying a transport.
- **GaudiHTTP Encoders**: Refactor `Http10ClientEncoder`, `Http11ClientEncoder`, `Http11ServerEncoder`,
  `Http2ClientEncoder`, `Http2ServerEncoder` to write against `IBufferWriter<byte>` instead of `WireBuffer`
  + `SpanWriter`. `IConnectionTransport`'s `GetMemory`/`Advance` pair satisfies `IBufferWriter<byte>`, so
  the same encoder method serves both the new pipe path and (via a `WireBuffer`-backed
  `IBufferWriter<byte>` adapter) the legacy path — no behavior fork needed inside the encoder itself.
- **GaudiHTTP Body Pumps**: `SerialBodyPump` (H1.0/H1.1) and `FlowControlledBodyPump` (H2) chunk delivery
  adapts to write into `_transport` + call `RequestFlush()` when pipe mode is active, falling back to
  `ops.OnOutbound(TransportData.Rent(...))` otherwise. `FlowControlledBodyPump`'s WINDOW_UPDATE gating is
  unchanged either way — only the wire-write mechanism forks.

## What Does NOT Change (out of scope for Phase C)

- The transport stage (`TcpConnectionStage`/`TcpConnectionStateMachine` in servus.akka) is **not** switched
  to pipe mode. Production traffic keeps flowing through `TransportData` on the port; `_transport` stays
  null outside of unit tests that inject a mock transport directly.
- `HttpClientConnectionStageLogic` / `HttpServerConnectionStageLogic` are **not** thinned (that is Phase D)
  beyond adding `Self`.
- No QUIC/H3 state machine, encoder, or body pump changes — `MultiplexedBodyPump`,
  `Http3OutboundWriter`, and the H3 client/server SMs are untouched.
- `TransportDataFlushed` / `OnOutboundFlushed` / watermark credit machinery is **not** removed yet — it
  keeps running for the legacy path and is only dropped once Phase D removes the fallback.

## Capabilities

### New Capabilities
- `sm-transport-io`: State-machine-owned transport I/O for TCP protocols — `RequestRead`/`RequestFlush`
  loop, generation-guarded async dispatch via `PipeTo(ops.Self)`, sync fast-path with a budget, and
  encoder writes against `IBufferWriter<byte>`. Scoped to the dual-path (pipe mode + legacy fallback)
  described above; the pipe path is exercised by unit tests only until Phase D wires up the transport
  stage.

### Modified Capabilities
- `protocol-state-machine-contract`: `IClientStageOperations`/`IServerStageOperations` gain `Self`.
  `DecodeServerData`/`DecodeClientData`, `OnOutbound`, and body-message handling gain a pipe-mode branch
  alongside the existing legacy behavior, which remains normative when `_transport` is null.
- `flow-control`: `SerialBodyPump`'s `TransportDataFlushed`-based credit gains a pipe-mode alternative
  (`_flushInProgress` gate via `FlushAsync`) used only when a transport is present; the existing credit
  system remains the active path in production until Phase D.

## Impact

- **GaudiHTTP** (`src/GaudiHTTP/`): `IClientStageOperations`, `IServerStageOperations`, all four TCP
  client SMs (H1.0, H1.1, H2 — not H3), all three TCP server SMs (H1.0, H1.1, H2 — not H3),
  `Http10ClientEncoder`, `Http11ClientEncoder`, `Http11ServerEncoder`, `Http2ClientEncoder`,
  `Http2ServerEncoder`, `SerialBodyPump`, `FlowControlledBodyPump`.
- **Tests**: New unit tests for the SM transport I/O base logic against a mock `IConnectionTransport`
  (sync fast-path, async PipeTo bridging, generation guard, `ShouldPauseNetwork` interaction). Encoder
  tests gain `IBufferWriter<byte>` (e.g. `ArrayBufferWriter<byte>`) coverage alongside existing
  `WireBuffer` coverage. Existing SM/stage tests are unaffected — legacy path behavior is unchanged.
- **Dependencies**: Depends on `pipe-phase-a-foundation` (`IConnectionTransport` exists) and
  `pipe-phase-b-decoder` (`FrameDecoder` accepts `ReadOnlySequence<byte>`) having landed first. Feeds
  `pipe-phase-d-wiring`, which switches the transport stage to pipe mode and removes the legacy fallback
  added here.
