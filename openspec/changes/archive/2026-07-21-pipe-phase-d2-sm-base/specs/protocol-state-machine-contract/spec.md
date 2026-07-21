## MODIFIED Requirements

### Requirement: Construction requires stage operations callback
State machines that operate over TCP MUST extend `TcpStateMachineBase<TOps>`, which owns transport
lifecycle dispatch, the read loop, flush tracking, and async result routing. The concrete SM
provides protocol-specific callbacks (decode, connect/disconnect handling, flush completion) and
implements `IClientStateMachine` or `IServerStateMachine`.

#### Scenario: Client state machine construction (updated)
- **WHEN** an `IClientStateMachine` for a TCP protocol is constructed
- **THEN** it MUST extend `TcpStateMachineBase<IClientStageOperations>`
- **AND** it MUST pass the `IClientStageOperations` instance to the base constructor
- **AND** it MUST NOT create a standalone `TransportIo` instance

#### Scenario: Server state machine construction (updated)
- **WHEN** an `IServerStateMachine` for a TCP protocol is constructed
- **THEN** it MUST extend `TcpStateMachineBase<IServerStageOperations>`
- **AND** it MUST pass the `IServerStageOperations` instance to the base constructor

---

### Requirement: Inbound data arrives via DecodeServerData / DecodeClientData (updated)
The base class owns lifecycle dispatch for `TransportConnected` and `TransportDisconnected`. The
concrete SM's `DecodeServerData`/`DecodeClientData` MUST call the base class's dispatch method
for lifecycle events. The base class calls back into the SM via `OnTransportConnected` /
`OnTransportDisconnected` abstract methods after updating internal state.

#### Scenario: TransportConnected routed through base class
- **WHEN** `TransportConnected` is received in `DecodeServerData` / `DecodeClientData`
- **THEN** the SM MUST call the base class lifecycle dispatch
- **THEN** the base class MUST initialize the transport, start the read loop, and call
  `OnTransportConnected`

#### Scenario: TransportData fallback for pre-connect
- **WHEN** `TransportData` is received and `Transport` is null
- **THEN** the SM MAY decode the buffer directly (pre-connect data buffered before `TransportConnected`)
- **WHEN** `TransportData` is received and `Transport` is not null
- **THEN** this MUST NOT happen in normal operation (all data arrives via the read loop)

---

### Requirement: Async body messages route through base class first
`OnBodyMessage(object msg)` MUST call the base class's `OnAsyncResult(msg)` first. If the base
class handles the message (returns true), the SM MUST NOT process it further. Only unhandled
messages (body pump events, SM-specific messages) are processed by the SM.

#### Scenario: ReadCompleted/FlushCompleted handled by base
- **WHEN** `OnBodyMessage` receives a `ReadCompleted` or `FlushCompleted` message
- **THEN** the base class `OnAsyncResult` MUST handle it and return true
- **AND** the SM MUST NOT process it further

## REMOVED Requirements

### Requirement: TransportDataFlushed credit signal for TCP
**Reason**: `TransportDataFlushed` was the outbound credit signal under the `Channel<WireBuffer>`
transport. With pipe-based transport, outbound backpressure is handled by `PipeWriter.FlushAsync`
blocking at the pause threshold, and the serial body pump receives credit via the
`onFlushCompleted` callback from `TransportIo.ProcessFlushResult`. No TCP source emits
`TransportDataFlushed` since Phase D removed it from the transport stage.
**Migration**: Remove `case TransportDataFlushed:` from all TCP SM `DecodeServerData`/
`DecodeClientData` methods. The `TransportDataFlushed` class in `ITransportInbound.cs` is retained
only if QUIC still uses it; otherwise delete it.
