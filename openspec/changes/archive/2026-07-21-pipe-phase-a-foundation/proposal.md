## Why

`pipe-transport-tcp` (master plan, `openspec/changes/pipe-transport-tcp/`) replaces the TCP transport's
`WireBuffer`/`Channel<WireBuffer>` data path with `System.IO.Pipelines`, and moves transport I/O ownership
into the GaudiHTTP protocol state machines. That is a large, cross-repo change. Phase A builds the
foundation in isolation: the `IConnectionTransport` abstraction, the pipe-backed implementation, and the
socket↔pipe pump tasks — all in `servus.akka` — with no consumer wired up yet. This lets the new
transport primitive be built, tested, and reviewed on its own before any protocol-layer code depends on
it, and keeps the (large) GaudiHTTP-side change out of this PR.

## What Changes

- **servus.akka Transport**: Add `IConnectionTransport` — a method-based interface wrapping a
  `PipeReader` (inbound) + `PipeWriter` (outbound) pair, exposing `ReadAsync`, `AdvanceTo` (two
  overloads), `GetMemory`, `Advance`, `FlushAsync`, `CompleteOutput`, `Info`, `Abort`. Raw
  `PipeReader`/`PipeWriter` are never exposed to callers.
- **servus.akka Transport**: Add `ConnectionTransport`, the concrete implementation, constructed from a
  `Pipe` pair and a `ConnectionInfo`.
- **servus.akka Transport**: Add static `TransportPumps.RunReadPump(Socket, PipeWriter, AdaptiveHint,
  CancellationToken)` and `TransportPumps.RunWritePump(PipeReader, Socket, CancellationToken)` —
  background tasks bridging socket I/O to/from the pipe pair. Read pump reuses the existing
  `AdaptiveHint` sizing pattern.
- **servus.akka Transport**: Add pipe threshold options (input/output pause/resume watermarks, segment
  size) to transport configuration, with the defaults from the master design (input 128KB/64KB, output
  256KB/128KB).
- **servus.akka Transport**: Extend `TransportConnected` with an optional `IConnectionTransport? Transport`
  member so a future consumer can receive it; existing `ConnectionInfo`-only construction remains valid
  (additive, non-breaking).
- **Tests**: Unit tests for `RunReadPump`/`RunWritePump` (data delivery, EOF, socket error, backpressure)
  and for `ConnectionTransport` (read/advance/write/flush/complete/abort contract).

## What Does NOT Change (out of scope for Phase A)

- No GaudiHTTP changes of any kind.
- `TcpConnectionStage` / `TcpConnectionStateMachine` do NOT switch to pipe-based data flow — `WireBuffer`,
  `Channel<WireBuffer>`, and the existing watermark/flush machinery keep running unchanged. Wiring
  `IConnectionTransport` into the transport stage is Phase D (`pipe-phase-d-wiring`) work.
- No `FrameDecoder` signature changes (Phase B, `pipe-phase-b-decoder`).
- No encoder changes.
- No protocol state machine changes (Phase C, `pipe-phase-c-sm-io`).

## Capabilities

### New Capabilities
- `pipe-transport-foundation`: `IConnectionTransport` interface, `ConnectionTransport` implementation,
  read/write pump tasks, and configurable pipe thresholds — the building blocks the later phases wire
  into the live transport/protocol path.

### Modified Capabilities
- None. `TransportConnected` gains an additive optional member; no existing capability's documented
  behavior changes.

## Impact

- **servus.akka** (`lib/servus.akka/src/Servus.Akka/Transport/`): new files for
  `IConnectionTransport`, `ConnectionTransport`, pump tasks, and pipe threshold options; `ITransportInbound.cs`
  gains the optional `Transport` member on `TransportConnected`. No existing transport behavior changes —
  this is purely additive.
- **GaudiHTTP**: none.
- **Tests**: new unit tests only, in servus.akka's test project. No existing test changes required.
- Downstream phases (`pipe-phase-b-decoder` through `pipe-phase-e-cleanup`) depend on this change landing
  first; see `openspec/changes/pipe-transport-tcp/design.md` for the full end-state design this phase is
  building toward.
