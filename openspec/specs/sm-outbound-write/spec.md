# SM Outbound Write

Direct pipe-writer outbound path for TCP state machines. When a TCP state machine has an active `IConnectionTransport`, all outbound byte data (encoded headers, body frames, control frames) is written directly to the pipe via `GetMemory`/`Advance`/`FlushAsync`, bypassing the `ITransportOutbound` network port entirely. Covers the write path, flush tracking ownership, and the elimination of `TransportData` from the connected TCP `OnOutbound` path.

## Requirements

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

---

### Requirement: Single flush tracking location
Flush-in-progress state MUST be tracked in exactly one location -- the `TransportIo` instance owned
by the base class. The stage logic MUST NOT maintain its own flush tracking
(`_flushInProgress`, `_flushGen`).

#### Scenario: Flush completes synchronously
- **WHEN** `FlushAsync` returns `IsCompletedSuccessfully`
- **THEN** the base class MUST process the result immediately without `PipeTo`
- **AND** the `onFlushCompleted` callback MUST fire (for serial pump credit)

#### Scenario: Flush completes asynchronously
- **WHEN** `FlushAsync` returns a pending `ValueTask`
- **THEN** the base class MUST `PipeTo(self)` with a `FlushCompleted`/`FlushFailed` message
- **AND** the stage logic MUST route the message to `_sm.OnBodyMessage` (existing path)
- **AND** the base class `OnAsyncResult` MUST handle it

---

### Requirement: No TransportData items flow through OnOutbound for connected TCP
Once a TCP connection is established, the `OnOutbound` method on `IClientStageOperations` /
`IServerStageOperations` MUST carry only lifecycle commands (`ConnectTransport`,
`DisconnectTransport`, `OpenStream`, `ResetStream`, `CompleteWrites`). `TransportData` items
MUST NOT be pushed through `OnOutbound` while a transport is active.

#### Scenario: Stage logic OnOutbound has no TransportData branch
- **WHEN** the stage logic receives an `ITransportOutbound` item via `OnOutbound`
- **THEN** it MUST push/queue the item to the network port without type-checking for `TransportData`
- **AND** no `BridgeTransportData` method MUST exist in the stage logic
