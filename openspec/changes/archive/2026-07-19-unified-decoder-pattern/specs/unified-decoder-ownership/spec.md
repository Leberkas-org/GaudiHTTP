## ADDED Requirements

### Requirement: H2 FrameDecoder accepts ReadOnlyMemory instead of WireBuffer
The H2 `FrameDecoder` SHALL accept `ReadOnlyMemory<byte>` input via `DecodeAll(ReadOnlyMemory<byte>, out int)` instead of taking WireBuffer ownership. The caller retains buffer ownership.

#### Scenario: Complete frames decoded from caller-owned memory
- **WHEN** a `ReadOnlyMemory<byte>` containing complete H2 frames is passed to `DecodeAll`
- **THEN** frames SHALL be decoded with identical results as the former `Decode(WireBuffer)` method
- **THEN** frame payloads SHALL be zero-copy slices of the caller's memory

#### Scenario: Caller disposes buffer after frame consumption
- **WHEN** the caller disposes the WireBuffer after iterating all returned frames
- **THEN** no use-after-free or data corruption SHALL occur

### Requirement: H2 FrameDecoder uses field-based remainder
Remainder bytes from incomplete frames SHALL be stored in a field-level `byte[]` (grow-on-demand, never-shrink) instead of retaining a WireBuffer.

#### Scenario: Partial frame buffered across two DecodeAll calls
- **WHEN** input ends mid-frame
- **AND** the next `DecodeAll` call provides the remaining bytes
- **THEN** the frame SHALL be decoded correctly from the combined data
- **THEN** no `WireBuffer.Rent` or `MemoryPool.Rent` SHALL be called for remainder storage

#### Scenario: Remainder compacted safely
- **WHEN** remainder exists from a previous call
- **THEN** compaction SHALL occur at the start of the next `DecodeAll` call (before any frame slices reference the buffer)

### Requirement: Zero-copy contract for complete frames
Frames fully contained in a single `DecodeAll` input (no remainder involved) SHALL reference the caller's memory directly — no copy.

#### Scenario: Frame payload aliases input buffer
- **WHEN** a complete DATA frame is in the input and no remainder exists
- **THEN** mutating the input buffer SHALL be visible through the frame's payload (aliasing proof)

#### Scenario: Frame spanning two inputs owns its data
- **WHEN** a frame is assembled from remainder + new input
- **THEN** mutating both input buffers SHALL NOT affect the frame's payload

### Requirement: Callers manage WireBuffer lifecycle
`Http2ClientSessionManager.DecodeFrames` and `Http2ServerSessionManager.DecodeClientData` SHALL retain WireBuffer ownership, pass `buffer.Memory` to the decoder, and dispose the buffer after frame processing.

#### Scenario: Client session manager owns buffer
- **WHEN** `DecodeFrames` is called with a WireBuffer
- **THEN** the WireBuffer SHALL be disposed after the returned frames have been consumed

#### Scenario: Server session manager owns buffer
- **WHEN** `DecodeClientData` is called with a WireBuffer
- **THEN** the WireBuffer SHALL be disposed after the frame processing loop completes (including on exception paths)

### Requirement: Functional parity with previous implementation
All frame parsing, validation, CONTINUATION state tracking, and error detection SHALL produce identical results.

#### Scenario: All existing H2 FrameDecoder tests pass
- **WHEN** the existing H2 FrameDecoder test suite runs against the refactored implementation
- **THEN** all tests SHALL pass (with necessary signature-change adaptations)

#### Scenario: All H2 integration tests pass
- **WHEN** the full End2End integration test suite runs
- **THEN** all H2-related tests SHALL pass with identical results
