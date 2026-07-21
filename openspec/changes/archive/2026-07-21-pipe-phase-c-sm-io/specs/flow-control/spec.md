## MODIFIED Requirements

### Requirement: SerialBodyPump outbound byte budget
`SerialBodyPump` (used by HTTP/1.1) applies a fixed outbound byte budget (`maxBytes`) to gate body reads
when `_transport` is null (legacy path), unchanged from prior behavior. When the owning state machine has a
non-null `_transport` (pipe mode), chunk delivery is instead gated by `_flushInProgress`: the pump MUST NOT
deliver a new chunk while a `FlushAsync()` triggered by the previous chunk's `RequestFlush()` is still
pending. The credit-counting fields (`_availableBytes`, `maxBytes`) remain in place and continue to serve
the legacy path; they are not consulted in pipe mode.

#### Scenario: Budget is seeded at registration (legacy)
- **WHEN** `Register` is called with a body stream and the owning SM's `_transport` is null
- **THEN** `_availableBytes` is set to `maxBytes`, unchanged from prior behavior

#### Scenario: Body reads debit the budget (legacy)
- **WHEN** a body chunk of `bytesRead` bytes is emitted and `_transport` is null
- **THEN** `_availableBytes` is decremented by `bytesRead`, unchanged from prior behavior

#### Scenario: TransportDataFlushed credits the budget (legacy)
- **WHEN** `OnCapacityAvailable(bytes)` is called after a real transport flush and `_transport` is null
- **THEN** `_availableBytes` is incremented by `bytes`, clamped to `maxBytes`, unchanged from prior behavior
- **AND** if the pump was parked (budget <= 0), a new read is attempted

#### Scenario: Body pump pauses when the pipe is full (pipe mode)
- **WHEN** the owning SM writes a body chunk into `_transport` and calls `RequestFlush()`
- **AND** `FlushAsync()` goes async (pipe above threshold), setting `_flushInProgress = true`
- **THEN** the pump MUST NOT deliver another chunk until `_flushInProgress` clears
- **AND** `_availableBytes`/`maxBytes` are not consulted for this decision

#### Scenario: Body pump resumes when the pipe drains (pipe mode)
- **WHEN** a `FlushCompleted` message clears `_flushInProgress`
- **THEN** the SM MUST signal the pump that outbound capacity is available
- **AND** the pump MAY deliver the next chunk

#### Scenario: ResetCredit restores full budget on reconnect (legacy)
- **WHEN** `ResetCredit()` is called after a connection is re-established and `_transport` is null
- **THEN** `_availableBytes` is restored to `maxBytes`, unchanged from prior behavior
- **AND** if a body stream is active, a read is attempted immediately

#### Scenario: Depleted budget prevents further reads (legacy)
- **WHEN** `_availableBytes <= 0` and `_transport` is null
- **THEN** `TryStartRead` returns without scheduling a read, even if a body stream is active and no read is
  in flight — unchanged from prior behavior

---

### Requirement: FlowControlledBodyPump (H2 outbound gating)
`FlowControlledBodyPump` MUST continue to gate body reads on `FlowController` send-window availability
(connection- and stream-level RFC 9113 windows), unchanged by this phase in either mode — WINDOW_UPDATE
accounting is orthogonal to the wire-write mechanism. Only the write call site changes: when the owning
SM's `_transport` is non-null, an approved chunk MUST be written into `_transport` via `GetMemory`/`Advance`
followed by `RequestFlush()`, instead of `ops.OnOutbound(TransportData.Rent(...))`.

#### Scenario: Window gating is unchanged regardless of transport mode
- **WHEN** a stream's body is registered with the pump
- **THEN** the stream is enqueued for reading only if both its stream send window and the connection send
  window are positive, exactly as before this change, independent of whether `_transport` is null

#### Scenario: Approved chunk is written to the transport (pipe mode)
- **WHEN** a body read completes and the flow controller has approved the reserved window, with
  `_transport != null`
- **THEN** the chunk MUST be encoded directly into `_transport` via `GetMemory`/`Advance`
- **AND** `RequestFlush()` MUST be called
- **AND** `flowController.Reserve`/`Refund` bookkeeping is unaffected by this write-path change

#### Scenario: Approved chunk is emitted via ops.OnOutbound (legacy)
- **WHEN** a body read completes and the flow controller has approved the reserved window, with
  `_transport == null`
- **THEN** the chunk MUST be wrapped in `TransportData` and emitted via `ops.OnOutbound`, unchanged from
  prior behavior
