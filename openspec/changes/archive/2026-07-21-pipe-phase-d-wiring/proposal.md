## Why

Phases A–C (`pipe-phase-a-foundation`, `pipe-phase-b-decoder`, `pipe-phase-c-sm-io`) built the pieces
in isolation: `IConnectionTransport` + pumps (servus.akka, unwired), `ReadOnlySequence<byte>` FrameDecoders,
and SM-owned transport I/O with a legacy `TransportData` fallback path kept alive so each phase stayed
independently green. None of it runs end-to-end yet — `TcpConnectionStateMachine.OnLeaseAcquired` still
uses `Channel<WireBuffer>`, and the GaudiHTTP stage logics still grab/push `TransportData` on the network
port alongside the new SM path. Phase D is the wiring phase: flip the TCP transport stage to pipe mode,
delete the dual-path fallback, and thin the stage logics down to pure Akka Streams plumbing. This is the
first point the full pipe path is exercised by the complete test suite (unit+stage, integration, submodule).

## What Changes

- **servus.akka Transport**: `TcpConnectionStateMachine.OnLeaseAcquired` creates an input+output `Pipe`
  pair, starts `TransportPumps.RunReadPump`/`RunWritePump`, wraps them in a `ConnectionTransport`, and
  pushes `TransportConnected(IConnectionTransport)`. `TcpConnectionStage` stops pushing `TransportData`
  on the TCP path — the network port carries lifecycle events only (`TransportConnected`,
  `TransportDisconnected`).
- **servus.akka Transport**: Pump tasks are monitored via `PipeTo(Self)`; pump completion or failure
  triggers `TransportDisconnected`. Reconnect completes the old pipe pair, starts a new pipe pair + pump
  tasks, and pushes a fresh `TransportConnected`.
- **GaudiHTTP Stage Logic**: `HttpClientConnectionStageLogic` and `HttpServerConnectionStageLogic` are
  thinned to pure plumbing — port handlers call `_sm.OnTransportEvent(item)`, StageActor messages call
  `_sm.OnAsyncResult(msg)`, timers delegate unchanged. No more `Grab`/`Push` of `TransportData` on the
  network port.
- **GaudiHTTP Protocol State Machines**: The Phase C dual-path fallback (legacy `TransportData` decode
  alongside `IConnectionTransport`) is deleted — SMs consume `IConnectionTransport` exclusively.
- **GaudiHTTP Server**: `CompleteAfterFlushingOutbound` is adapted from queue-drain to
  `_transport.CompleteOutput()`.
- **Bug fixes**: Cherry-pick the transport-independent fixes from `feat/outbound-flow-control`
  (PendingRequest stale-cancel, reconnect body truncation, body-pump in-flight teardown) — the
  TCP-specific credit machinery from that branch is superseded by Pipe-native backpressure and is not
  ported.

## What Does NOT Change (out of scope for Phase D)

- QUIC/H3 transport (`MultiplexedData`, per-stream lifecycle) is unaffected — this phase is TCP only.
- No further FrameDecoder or encoder signature changes (done in Phases B/C).
- No new `IConnectionTransport` members (interface frozen since Phase A).

## Capabilities

### Modified Capabilities
- `streams-stages-pipeline`: TCP network port carries lifecycle events only; client and server stage
  logics become thin Akka Streams adapters; server completion uses `IConnectionTransport.CompleteOutput()`.

## Impact

- **servus.akka** (`lib/servus.akka/src/Servus.Akka/Transport/`): `TcpConnectionStateMachine`,
  `TcpConnectionStage`.
- **GaudiHTTP** (`src/GaudiHTTP/Streams/Stages/`): `HttpClientConnectionStageLogic`,
  `HttpServerConnectionStageLogic`, and the H1.0/H1.1/H2 client + server state machines (dual-path
  removal).
- **Tests**: full unit+stage suite (~6000), integration Client/End2End/Server (~680), servus.akka
  submodule (~750) — all must pass green; this is the first end-to-end exercise of the pipe path.
- **Risk**: highest of the four phases — first time pipe mode runs under real socket I/O and reconnect
  churn. Green criterion is the full suite, not a subset.
