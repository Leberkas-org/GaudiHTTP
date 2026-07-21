## ADDED Requirements

### Requirement: State machine owns the transport read loop when a transport is present
For TCP protocols (H1.0, H1.1, H2), when the state machine holds a non-null `IConnectionTransport`
(`_transport != null`), it MUST call `IConnectionTransport.ReadAsync()` directly and process the returned
`ReadResult` synchronously, instead of waiting for `TransportData` on the port. The SM MUST bridge async
completions to the actor thread via `PipeTo(_ops.Self, ...)`. When `_transport` is null, `RequestRead()`
MUST be a no-op — inbound data continues to arrive via the legacy port path (see
`protocol-state-machine-contract`).

#### Scenario: Sync fast-path when pipe has buffered data
- **WHEN** `_transport != null` and `ReadAsync()` completes synchronously (`IsCompletedSuccessfully`)
- **AND** the sync read budget has not been exhausted
- **THEN** the SM MUST process the result directly (no PipeTo, no actor message)
- **AND** the SM MUST decrement the sync budget and call `RequestRead()` again

#### Scenario: Async path when pipe is empty
- **WHEN** `_transport != null` and `ReadAsync()` does not complete synchronously
- **THEN** the SM MUST call `vt.PipeTo(_ops.Self, success: ..., failure: ...)`
- **AND** the SM MUST reset the sync read budget to `MaxSyncReads` (8)
- **AND** the SM MUST set `_readInProgress = true` to prevent concurrent reads

#### Scenario: Sync budget prevents actor starvation
- **WHEN** the sync read budget reaches 0
- **THEN** the SM MUST fall through to the PipeTo async path even if `IsCompletedSuccessfully`
- **AND** this yields the actor thread so other actors can process messages

#### Scenario: Read is not started when ShouldPauseNetwork is true
- **WHEN** `_transport != null` and `ShouldPauseNetwork` returns true (body reader full)
- **THEN** `RequestRead()` MUST NOT call `ReadAsync()`
- **AND** the SM MUST re-arm the read when `ShouldPauseNetwork` clears (e.g., on `BodyResumed`)

#### Scenario: RequestRead is a no-op without a transport
- **WHEN** `_transport` is null
- **THEN** `RequestRead()` MUST return immediately without side effects
- **AND** inbound data keeps arriving through `DecodeServerData`/`DecodeClientData` as before this change

---

### Requirement: State machine owns the transport flush cycle when a transport is present
When `_transport != null`, the state machine MUST call `IConnectionTransport.FlushAsync()` after writing
outbound data via `GetMemory`/`Advance`, and bridge async flush completions to the actor thread via
`PipeTo(_ops.Self, ...)`. When `_transport` is null, outbound data continues to be emitted via
`ops.OnOutbound(TransportData)` and `RequestFlush()` MUST be a no-op.

#### Scenario: Sync flush when pipe is below threshold
- **WHEN** `_transport != null` and `FlushAsync()` completes synchronously (pipe below
  `pauseWriterThreshold`)
- **THEN** the SM MUST process the result directly (no PipeTo)
- **AND** outbound capacity is immediately available for more writes

#### Scenario: Async flush when pipe is above threshold
- **WHEN** `_transport != null` and `FlushAsync()` does not complete synchronously (pipe above threshold)
- **THEN** the SM MUST call `vt.PipeTo(_ops.Self, success: ..., failure: ...)`
- **AND** the SM MUST set `_flushInProgress = true`
- **AND** the SM MUST NOT write additional data until the flush completes

#### Scenario: Flush completion resumes outbound capacity
- **WHEN** a `FlushCompleted` message arrives via `OnAsyncResult` with a valid generation
- **THEN** the SM MUST set `_flushInProgress = false`
- **AND** the SM MUST notify the body pump that outbound capacity is available

#### Scenario: Flush completion with IsCompleted signals connection loss
- **WHEN** a `FlushCompleted` arrives with `FlushResult.IsCompleted = true`
- **THEN** the SM MUST treat this as a connection loss

#### Scenario: RequestFlush is a no-op without a transport
- **WHEN** `_transport` is null
- **THEN** `RequestFlush()` MUST return immediately without side effects

---

### Requirement: Generation guard protects against stale async results
Each `TransportConnected` event (whether or not it carries a transport) MUST increment a transport
generation counter (`_transportGen`) in the SM. PipeTo transform delegates MUST capture the generation at
creation time. On async result dispatch, the SM MUST compare the message's generation with the current
generation and drop mismatches.

#### Scenario: Stale ReadCompleted from an old transport is dropped
- **WHEN** a `ReadCompleted` message arrives with a generation older than `_transportGen`
- **THEN** the SM MUST ignore the message (no decode, no re-arm)

#### Scenario: Stale FlushCompleted from an old transport is dropped
- **WHEN** a `FlushCompleted` message arrives with a generation older than `_transportGen`
- **THEN** the SM MUST ignore the message (no resume, no error)

#### Scenario: New TransportConnected resets read/flush state
- **WHEN** `TransportConnected` arrives (carrying a transport or not)
- **THEN** the SM MUST increment `_transportGen`
- **AND** the SM MUST set `_readInProgress = false` and `_flushInProgress = false`
- **AND** if the event carries a transport, the SM MUST create new cached `PipeReadState`/`PipeFlushState`
  delegate holders scoped to the new generation

---

### Requirement: Async result dispatch via OnAsyncResult
The SM MUST expose a single `OnAsyncResult(object msg)` method that the stage calls when the StageActor
receives a message. The SM dispatches internally based on message type: `ReadCompleted`, `ReadFailed`,
`FlushCompleted`, `FlushFailed`, or body pump messages.

#### Scenario: ReadCompleted dispatches to OnReadCompleted
- **WHEN** `OnAsyncResult` receives a `ReadCompleted` with valid generation
- **THEN** the SM MUST call its internal `OnReadCompleted(ReadResult)` method
- **THEN** `OnReadCompleted` MUST call `DecodeData(ReadOnlySequence<byte>)` to process the bytes
- **THEN** the SM MUST call `_transport.AdvanceTo(consumed, examined)` after decode, then `RequestRead()`

#### Scenario: FlushCompleted dispatches to OnFlushCompleted
- **WHEN** `OnAsyncResult` receives a `FlushCompleted` with valid generation
- **THEN** the SM MUST call its internal `OnFlushCompleted(FlushResult)` method

#### Scenario: Unknown messages dispatch to OnBodyMessage
- **WHEN** `OnAsyncResult` receives a message that is not Read/Flush related
- **THEN** the SM MUST forward it to `OnBodyMessage(msg)` (body pump coordination)

---

### Requirement: IClientStageOperations / IServerStageOperations expose Self
The stage operations interface MUST expose `IActorRef Self` — the StageActor reference. The SM uses this
for `PipeTo` calls when a transport is present. The stage operations MUST NOT expose `BridgeAsync` or
similar abstractions. This member is added in this phase even though production traffic does not yet set
`_transport` — it exists so `OnAsyncResult`/`PipeTo` can be exercised end-to-end by unit tests ahead of the
Phase D stage switch.

#### Scenario: SM uses ops.Self for PipeTo
- **WHEN** the SM (with a non-null `_transport`) needs to bridge an async `ValueTask` to the actor thread
- **THEN** it calls `vt.PipeTo(_ops.Self, success: ..., failure: ...)`
- **AND** the result arrives as a message dispatched to `OnAsyncResult`

#### Scenario: Self is implemented by both client and server stage logic
- **WHEN** `HttpClientConnectionStageLogic` or `HttpServerConnectionStageLogic` constructs its operations
  implementation
- **THEN** `Self` MUST return the owning `StageActor`'s `IActorRef`

---

### Requirement: Encoder writes against IBufferWriter<byte>
Outbound encoders (`Http10ClientEncoder`, `Http11ClientEncoder`, `Http11ServerEncoder`,
`Http2ClientEncoder`, `Http2ServerEncoder`) MUST expose an encode method that writes frame headers and body
data into an `IBufferWriter<byte>`, rather than a `WireBuffer`/`SpanWriter`. This single signature MUST
serve both the pipe-mode call site (passing `_transport` or a thin `IBufferWriter<byte>` wrapper over it)
and the legacy call site (passing a `WireBuffer`-backed `IBufferWriter<byte>` adapter) — the encoder itself
MUST NOT branch on which mode is active.

#### Scenario: Encoder writes into the supplied buffer writer
- **WHEN** the SM encodes a response (server) or request (client)
- **THEN** the encoder MUST call `writer.GetMemory(sizeHint)` to obtain writable memory
- **AND** write frame bytes into the returned `Memory<byte>`
- **AND** call `writer.Advance(bytesWritten)` to commit

#### Scenario: Pipe mode passes the transport directly
- **WHEN** `_transport != null`
- **THEN** the SM MUST pass `_transport` (or a wrapper exposing its `GetMemory`/`Advance` as
  `IBufferWriter<byte>`) to the encoder
- **AND** the SM MUST call `RequestFlush()` after encoding to trigger `FlushAsync`

#### Scenario: Legacy mode passes a WireBuffer-backed adapter
- **WHEN** `_transport` is null
- **THEN** the SM MUST pass a `WireBuffer`-backed `IBufferWriter<byte>` adapter to the encoder
- **AND** the SM MUST wrap the adapter's written bytes into `TransportData` and call
  `ops.OnOutbound(TransportData)`, unchanged from prior behavior

#### Scenario: Multiple frames batched before flush (pipe mode)
- **WHEN** the SM encodes multiple frames (e.g., HEADERS + DATA for a small response) with `_transport !=
  null`
- **THEN** all frames MUST be written via `GetMemory`/`Advance` before calling `RequestFlush()`
- **AND** a single `FlushAsync` covers all written frames (coalesced write)
