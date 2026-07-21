## MODIFIED Requirements

### Requirement: Stage logic is pure plumbing for TCP connections
`HttpClientConnectionStageLogic` and `HttpServerConnectionStageLogic` MUST NOT contain transport
data bridging, flush tracking, or outbound data interception. Their responsibilities are:

1. **Port handlers** — grab lifecycle items from network port, route to SM.
2. **StageActor message routing** — route all messages to `_sm.OnBodyMessage(msg)`.
3. **Timer delegation** — route timer fires to `_sm.OnTimerFired(name)`.
4. **OnOutbound** — push/queue `ITransportOutbound` items to the network port (lifecycle commands
   only for TCP; no `TransportData` interception).

#### Scenario: OnOutbound pushes without type-checking (updated)
- **WHEN** the SM calls `_ops.OnOutbound(item)` with any `ITransportOutbound`
- **THEN** the stage logic MUST push (or queue) the item to the outbound network port
- **AND** the stage logic MUST NOT check whether the item is `TransportData`
- **AND** no `BridgeTransportData` method MUST exist

#### Scenario: PostStop does not dispose TransportData from queue (updated)
- **WHEN** the stage logic tears down in `PostStop`
- **THEN** the `_outboundQueue` drain loop MUST NOT check for `TransportData` items
- **AND** no `WireBuffer` disposal MUST occur in the stage logic

#### Scenario: Stage logic does not track flush state (updated)
- **WHEN** outbound data is flushed to the pipe
- **THEN** the stage logic MUST NOT maintain `_flushInProgress`, `_flushGen`, or `_transport` fields
- **AND** no `BridgeFlushCompleted` / `BridgeFlushFailed` messages MUST exist

## REMOVED Requirements

### Requirement: Stage logic bridges TransportData to pipe
**Reason**: The `BridgeTransportData` method in both stage logics was a Phase D workaround for SMs
that still emitted `TransportData` via `OnOutbound`. With all TCP SMs writing directly to the pipe
via `TcpStateMachineBase`, no `TransportData` items arrive at `OnOutbound` during connected
operation. The bridge, its flush tracking, and its message types are dead code.
**Migration**: Delete `BridgeTransportData`, `BridgeFlushCompleted`, `BridgeFlushFailed`, stage-level
`_flushInProgress`/`_flushGen`/`_transport` fields, and the `TransportData` type-check in
`OnOutbound`. Delete `BridgeFlushCompleted`/`BridgeFlushFailed` from `PipeIoMessages.cs`.
