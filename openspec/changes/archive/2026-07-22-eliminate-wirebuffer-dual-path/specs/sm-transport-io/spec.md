## MODIFIED Requirements

### Requirement: State machine owns the transport read loop when a transport is present
For TCP protocols (H1.0, H1.1, H2), the state machine MUST call `IConnectionTransport.ReadAsync()`
directly and process the returned `ReadResult` synchronously. There is no fallback path — byte data
can only arrive through the pipe read loop. When `_transport` is null (before connect or after
disconnect), no data processing occurs.

#### Scenario: Sync fast-path when pipe has buffered data
- **WHEN** `_transport != null` and `ReadAsync()` completes synchronously (`IsCompletedSuccessfully`)
- **AND** the sync read budget has not been exhausted
- **THEN** the SM MUST process the result directly (no PipeTo, no actor message)
- **AND** the SM MUST decrement the sync budget and call `RequestRead()` again

#### Scenario: Async path when pipe is empty
- **WHEN** `_transport != null` and `ReadAsync()` does not complete synchronously
- **THEN** the SM MUST bridge to the actor thread with a `ReadCompleted`/`ReadFailed` message
- **AND** the SM MUST reset the sync read budget to `MaxSyncReads` (8)
- **AND** the SM MUST set `_readInProgress = true` to prevent concurrent reads

#### Scenario: Sync budget prevents actor starvation
- **WHEN** the sync read budget reaches 0
- **THEN** the SM MUST fall through to the async path even if `IsCompletedSuccessfully`
- **AND** this yields the actor thread so other actors can process messages

#### Scenario: Read is not started when ShouldPauseNetwork is true
- **WHEN** `_transport != null` and `ShouldPauseNetwork` returns true (body reader full)
- **THEN** `RequestRead()` MUST NOT call `ReadAsync()`
- **AND** the SM MUST re-arm the read when `ShouldPauseNetwork` clears

#### Scenario: No data path without transport
- **WHEN** `_transport` is null
- **THEN** `RequestRead()` MUST return immediately without side effects
- **AND** no byte data can be delivered to the SM through any path
- **AND** `DecodeServerData`/`DecodeClientData` MUST only handle lifecycle events

### Requirement: State machine owns the transport flush cycle when a transport is present
When `_transport != null`, the state machine MUST call `IConnectionTransport.FlushAsync()` after
writing outbound data via `GetMemory`/`Advance`. When `_transport` is null, `RequestFlush()` MUST
be a no-op — there is no fallback outbound path.

#### Scenario: Sync flush when pipe is below threshold
- **WHEN** `_transport != null` and `FlushAsync()` completes synchronously
- **THEN** the SM MUST process the result directly (no PipeTo)
- **AND** outbound capacity is immediately available for more writes

#### Scenario: Async flush when pipe is above threshold
- **WHEN** `_transport != null` and `FlushAsync()` does not complete synchronously
- **THEN** the SM MUST bridge to the actor thread with a `FlushCompleted`/`FlushFailed` message
- **AND** the SM MUST set `_flushInProgress = true`
- **AND** the SM MUST NOT write additional data until the flush completes
