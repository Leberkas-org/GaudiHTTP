## MODIFIED Requirements

### Requirement: Client engine composition
For TCP protocols (H1.0, H1.1, H2), the network port between protocol stage and transport stage
carries ONLY lifecycle events. `TestConnectionStage` MUST deliver a `TestPipeTransport` via
`TransportConnected` for TCP protocol tests, and byte data MUST flow through the pipe, not the
Akka Streams port. `TransportData` items MUST NOT appear on the port for TCP protocols in either
direction.

#### Scenario: TCP stage-test uses pipe transport for data
- **WHEN** a TCP protocol stage-test uses `TestConnectionStage` with `AutoConnectWithTransport()`
- **THEN** `TransportConnected` MUST carry a non-null `IConnectionTransport`
- **AND** the SM MUST read inbound data via `transport.ReadAsync()`
- **AND** the SM MUST write outbound data via `transport.GetMemory`/`Advance`/`FlushAsync`
- **AND** no `TransportData` items MUST flow through the Akka Streams port

#### Scenario: Test feeds response data via pipe transport
- **WHEN** a stage-test needs to provide response data to the SM
- **THEN** it MUST call `stage.Transport!.FeedInput(data)` instead of `stage.PushData(data)`
- **AND** the SM receives it via its pipe read loop

#### Scenario: Test reads request data from pipe transport
- **WHEN** a stage-test needs to capture encoded request data from the SM
- **THEN** it MUST call `stage.Transport!.ReadOutputAsync()` or `TryReadOutput()`
- **AND** no `TransportData` items are available via `stage.ReceivedOutbound`
