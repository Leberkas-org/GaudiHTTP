# SM Outbound Write

Direct pipe-writer outbound path for TCP state machines. When a TCP state machine has an active `IConnectionTransport`, all outbound byte data (encoded headers, body frames, control frames) is written directly to the pipe via `GetMemory`/`Advance`/`FlushAsync`, bypassing the `ITransportOutbound` network port entirely. Covers the write path, flush tracking ownership, and the elimination of `TransportData` from the connected TCP `OnOutbound` path.

## Requirements

### Requirement: SM writes outbound data directly to PipeWriter when connected
When a TCP state machine has an active `IConnectionTransport` (post-`TransportConnected`), all
outbound byte data (encoded headers, body frames, control frames) MUST be written to
`IConnectionTransport.GetMemory`/`Advance` and flushed via `FlushAsync`, bypassing the
`ITransportOutbound` network port entirely.

For H1 protocols, encoders MUST receive a `TransportBufferWriter` wrapping the transport directly --
no intermediate `WireBuffer` is allocated or copied. For H2, the `SessionManager` serializes frames
into a reusable scratch buffer and invokes `EmitData(ReadOnlySpan<byte>)` on the SM, which copies
into the pipe. The `EmitWireBuffer` helper method is eliminated from all TCP SMs.

#### Scenario: H1 connected SM writes headers directly to pipe via TransportBufferWriter
- **WHEN** a TCP H1 client SM encodes request headers while `Transport` is not null
- **THEN** the SM MUST create a `TransportBufferWriter(Transport)` and pass it to the encoder
- **AND** the encoder MUST write directly into the pipe buffer via `IBufferWriter<byte>`
- **AND** `RequestFlush()` MUST be called after the write
- **AND** no `WireBuffer.Rent` MUST be called
- **AND** no `WireBufferWriter` MUST be used

#### Scenario: H1 server response with coalesced body writes sequentially to pipe
- **WHEN** a TCP H1.1 server SM encodes a response with a buffered body eligible for coalescing
- **THEN** the SM MUST write headers via `TransportBufferWriter` first
- **AND** then write the buffered body bytes via `transport.GetMemory`/`Advance`
- **AND** call `RequestFlush()` once after both writes
- **AND** the pipe's internal buffering MUST coalesce both writes into a single flush

#### Scenario: H2 SessionManager serializes frames into scratch buffer
- **WHEN** an H2 SessionManager needs to emit encoded frames
- **THEN** it MUST serialize all frames into a reusable per-SessionManager scratch buffer
- **AND** call `EmitData(ReadOnlySpan<byte>)` with the serialized bytes
- **AND** the SM's `EmitData` callback MUST copy the span into the pipe via `GetMemory`/`Advance`
- **AND** `RequestFlush()` MUST be called after the copy

#### Scenario: Connected SM writes body data directly to pipe
- **WHEN** a TCP SM emits body data frames while `Transport` is not null
- **THEN** the body bytes MUST be written to `Transport.GetMemory`/`Advance`
- **AND** `RequestFlush()` MUST be called after the write
- **AND** no `WireBuffer.Rent` MUST be called for the body data path

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
- **THEN** the base class MUST bridge to the actor thread with a `FlushCompleted`/`FlushFailed` message
- **AND** the SM MUST set `_flushInProgress = true`
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
- **AND** the item MUST be a lifecycle command, never a `TransportData`
