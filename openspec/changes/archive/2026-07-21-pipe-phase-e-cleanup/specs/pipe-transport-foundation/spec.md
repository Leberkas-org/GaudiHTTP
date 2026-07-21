## MODIFIED Requirements

### Requirement: IConnectionTransport is the sole TCP data path
`IConnectionTransport` (added in `pipe-phase-a-foundation`, wired up as the exclusive TCP consumer path in
`pipe-phase-d-wiring`) is now the only data path for TCP connections — the legacy fallback machinery it
was built alongside (kept compiled-but-unreachable through Phase D for safety) is deleted in this phase.
Callers on the TCP path MUST use `IConnectionTransport` exclusively; no TCP code path may construct or
consume `WireBuffer`, `TransportData`, or `TransportDataFlushed`.

#### Scenario: No TCP call site constructs WireBuffer
- **WHEN** a TCP connection sends or receives data
- **THEN** the send/receive path MUST go through `IConnectionTransport.GetMemory`/`Advance`/`FlushAsync`
  (outbound) or `IConnectionTransport.ReadAsync`/`AdvanceTo` (inbound)
- **AND** no TCP-path code MUST call `WireBuffer.Rent`

#### Scenario: No TCP call site dispatches TransportData
- **WHEN** the TCP transport stage reports a new connection or data event
- **THEN** the network port between transport stage and protocol stage MUST carry only
  `TransportConnected(IConnectionTransport)` and `TransportDisconnected(reason)`
- **AND** `TransportData`/`TransportDataFlushed` MUST NOT be created or dispatched on the TCP path

#### Scenario: WireBuffer and TransportData remain available for QUIC
- **WHEN** a QUIC/H3 connection sends or receives data
- **THEN** `WireBuffer` and `TransportData` remain fully functional and unmodified for the QUIC path
- **AND** no behavior described in this requirement applies to QUIC/H3

## REMOVED Requirements

### Requirement: Dual-path fallback for TCP transport I/O
**Reason**: `pipe-phase-c-sm-io` introduced a dual-path (pipe mode + legacy `TransportData` fallback) so
state machines could support both paths side by side while the transport stage still ran in legacy mode.
`pipe-phase-d-wiring` flipped the transport stage to pipe mode and removed the SM-side fallback branch,
but the underlying legacy machinery it branched away from (`Channel<WireBuffer>`, watermark credit,
`ReadEventState`, the old `ReceiveAsync` loop, `SendFlushed`/`OnFlushed`) remained physically present,
unreachable, in `servus.akka`. This phase deletes that machinery outright — there is no remaining
consumer of the fallback path to preserve.
**Migration**: No caller-visible migration — the fallback path was already unreachable after
`pipe-phase-d-wiring`. This is pure deletion of code with zero live callers, confirmed via
`find_references`/`find_dead_code` per this change's `design.md` Decision 4.
