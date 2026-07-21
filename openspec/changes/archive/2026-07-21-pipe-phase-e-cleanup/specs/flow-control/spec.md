## REMOVED Requirements

### Requirement: TransportDataFlushed-based outbound credit for serial pump
**Reason**: Superseded by Pipe-native `FlushAsync` backpressure, which has been the active TCP mechanism
since `pipe-phase-d-wiring`. The `TransportDataFlushed` message, `SendFlushed` event, `_bytesInFlight`
tracking, and high/low watermark system were kept alive (unreachable) through Phase D for safety; Phase E
deletes them outright now that a full green suite confirms nothing on the TCP path still depends on them.
**Migration**: Delete `TransportDataFlushed` handling from H1.0/H1.1/H2 client and server state machines
(if any residual references remain post-Phase-D). Delete `_bytesInFlight`, `_highWatermark`,
`_lowWatermark` from `TcpConnectionStateMachine`. Delete `SendFlushed`/`OnFlushed` callback wiring for TCP
connections (split from `DuplexConnectionBase` if QUIC shares the base class — see this change's
`design.md` Decision 2). H2 RFC 9113 WINDOW_UPDATE flow control and H3/QUIC `MultiplexedDataFlushed` are
unaffected — this removal is TCP-only.

### Requirement: Legacy TCP receive loop and outbound channel
**Reason**: The read pump (inbound) and write pump (outbound), wired up in `pipe-phase-a-foundation`
through `pipe-phase-d-wiring`, fully replace the `Channel<WireBuffer>` outbound queue and the
`ReceiveAsync(WireBuffer)` + `PipeTo(ReadCompleted)` inbound loop for TCP connections. `ReadEventState`,
the cached-delegate holder for the old inbound loop, is superseded by `PipeReadState`/`PipeFlushState`.
**Migration**: Delete `Channel<WireBuffer>` outbound queue and its send loop from the TCP path. Delete the
old `ReceiveAsync(WireBuffer)` loop from `TcpConnectionStateMachine`. Delete `ReadEventState`. Delete
`WireBuffer.Rent` call sites from TCP send/receive (the `WireBuffer` class itself is retained for QUIC).
Delete `TransportData` creation/dispatch from the TCP transport stage — the network port between
transport and protocol stage carries lifecycle events only (`TransportConnected`, `TransportDisconnected`),
per `pipe-phase-d-wiring`.
