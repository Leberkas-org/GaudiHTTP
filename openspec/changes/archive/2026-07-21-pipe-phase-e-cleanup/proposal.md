## Why

Phase D (`pipe-phase-d-wiring`) flipped the TCP transport stage to pipe mode and deleted the Phase C
dual-path fallback in the protocol state machines — the full pipe path now carries all TCP production
traffic and the full suite is green. What's left is dead: `WireBuffer` rent/return call sites,
`TransportData`/`TransportDataFlushed` dispatch, the `Channel<WireBuffer>` outbound queue, the
watermark-based credit system (`_bytesInFlight`/`_highWatermark`/`_lowWatermark`), `SendFlushed`/
`OnFlushed`, `ReadEventState`, and the old `ReceiveAsync` loop are all still physically present in
`servus.akka` and GaudiHTTP but structurally unreachable on the TCP path. Phase E is the cleanup sweep
that removes them — no functional change, no new capability, just deleting code the pipe path made
obsolete.

## What Changes

- **servus.akka Transport**: Remove `WireBuffer` rent/return call sites from the TCP data path (send and
  receive). The `WireBuffer` class itself stays — QUIC still uses it.
- **servus.akka Transport**: Remove `Channel<WireBuffer>` outbound queue, `SendFlushed` event, and
  `OnFlushed` callback from `DuplexConnectionBase`/`TcpConnectionStateMachine`. If `DuplexConnectionBase`
  is shared with QUIC, split out the TCP-only channel/callback members or gate them so QUIC's usage is
  unaffected.
- **servus.akka Transport**: Remove `_bytesInFlight`, `_highWatermark`, `_lowWatermark` fields and the
  watermark-crossing logic from `TcpConnectionStateMachine` — superseded by Pipe `pauseWriterThreshold`/
  `resumeWriterThreshold` since Phase D.
- **servus.akka Transport**: Remove `ReadEventState` — superseded by `PipeReadState`/`PipeFlushState`
  (Phase C) in the SM.
- **servus.akka Transport**: Remove the old `ReceiveAsync(WireBuffer)` + `PipeTo(ReadCompleted)` loop from
  `TcpConnectionStateMachine` — superseded by the read pump (Phase A/D).
- **GaudiHTTP Stage Operations**: Remove `ops.OnOutbound` overloads/call sites that push raw TCP byte data
  (`TransportData`). Keep `OnOutbound` for lifecycle commands and for the QUIC/H3 path.
- **GaudiHTTP Stage Logic**: Remove `_outboundQueue` entries that queued outbound `TransportData` items in
  `HttpClientConnectionStageLogic`/`HttpServerConnectionStageLogic`. Keep the queue (or its lifecycle-only
  remnant) for connection lifecycle commands.
- **GaudiHTTP Transport Dispatch**: Remove `TransportData` creation/dispatch and `TransportDataFlushed`
  emission/handling from the TCP path end to end (transport stage, network port, SM). `TransportData` and
  `ITransportInbound`/`ITransportOutbound` stay — QUIC still uses them.
- **Audit pass**: sweep for dead imports (`System.Threading.Channels` where no longer needed, unused
  `WireBuffer` usings), unused fields left behind by the above removals, and orphaned test helpers/mocks
  that only existed to script the deleted paths (e.g. `TransportData`-scripting helpers in TCP SM tests,
  `Channel<WireBuffer>` test doubles).

## What Does NOT Change (out of scope for Phase E)

- `WireBuffer`, `TransportData`, `ITransportInbound`, `ITransportOutbound` classes/interfaces are NOT
  deleted — QUIC/H3 depends on all four.
- No QUIC/H3 behavior changes — `MultiplexedData`, `MultiplexedDataFlushed`, per-stream lifecycle untouched.
- No new capability, no wire-format change, no behavioral change on any green test. This phase is
  subtractive only.
- No `IConnectionTransport` interface changes (frozen since Phase A).

## Capabilities

### Modified Capabilities
- `flow-control`: The `TransportDataFlushed`-based outbound credit for the TCP serial pump is removed
  (REMOVED requirement) — Pipe-native `FlushAsync` backpressure, active since Phase D, is now the only
  TCP mechanism. H2 WINDOW_UPDATE flow control and H3/QUIC `MultiplexedDataFlushed` are unaffected.
- `pipe-transport-foundation`: Documents that the old TCP transport paths (`Channel<WireBuffer>`,
  watermark credit, `ReadEventState`, legacy `ReceiveAsync`) this capability's SM/stage consumers used to
  fall back to are removed; `IConnectionTransport` is the sole TCP data path.

## Impact

- **servus.akka** (`lib/servus.akka/src/Servus.Akka/Transport/`): `TcpConnectionStateMachine`,
  `DuplexConnectionBase`, `TcpConnectionStage`, `ReadEventState` (deleted), `WireBuffer` TCP call sites
  (deleted, class stays).
- **GaudiHTTP** (`src/GaudiHTTP/Streams/Stages/`): `HttpClientConnectionStageLogic`,
  `HttpServerConnectionStageLogic`, `IClientStageOperations`, `IServerStageOperations` — remove
  data-carrying `OnOutbound` overloads and `_outboundQueue` data items; keep lifecycle-command usage.
- **Tests**: full unit+stage suite (~6000+), integration Client/End2End/Server, servus.akka submodule —
  all must stay green (this phase removes code, it does not change behavior — any red test indicates the
  "dead" code was not actually dead). Remove test helpers/mocks that only exercised deleted paths.
- **Dependencies**: depends on `pipe-phase-d-wiring` having landed (full pipe path wired, dual-path
  fallback already removed, full suite green). This is the terminal phase of the `pipe-transport-tcp`
  master plan.
