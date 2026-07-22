## MODIFIED Requirements

### Requirement: SM writes outbound data directly to PipeWriter when connected
When a TCP state machine has an active `IConnectionTransport` (post-`TransportConnected`), all
outbound byte data MUST be written to `IConnectionTransport.GetMemory`/`Advance` and flushed via
`FlushAsync`. There is no fallback path — `Transport` MUST NOT be null when the SM produces
outbound data.

#### Scenario: Connected SM writes headers directly to pipe
- **WHEN** a TCP SM encodes request/response headers
- **THEN** the encoded bytes MUST be written to `Transport.GetMemory`/`Advance`
- **AND** `RequestFlush()` MUST be called after the write
- **AND** no `WireBuffer.Rent` MUST be called
- **AND** no `Ops.OnOutbound(TransportData)` MUST be called

#### Scenario: Connected SM writes body data directly to pipe
- **WHEN** a TCP SM emits body data frames
- **THEN** the body bytes MUST be written to `Transport.GetMemory`/`Advance`
- **AND** `RequestFlush()` MUST be called after the write

#### Scenario: Transport is always present when SM writes
- **WHEN** a TCP SM's write method is called (header encoding, body emit, frame emit)
- **THEN** `Transport` MUST NOT be null
- **AND** no `if (Transport is null)` guard MUST exist in the write path
