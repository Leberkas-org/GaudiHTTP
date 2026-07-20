## ADDED Requirements

### Requirement: H3 FrameDecoder adopts inbound WireBuffer without allocation
The H3 `FrameDecoder` SHALL adopt the incoming `WireBuffer` as a field-level `_workingBuffer` instead of renting from `MemoryPool<byte>.Shared`. The previous buffer SHALL be disposed when a new buffer is adopted on the next `Decode()` call. No `MemoryPool.Rent` SHALL occur on the hot decode path.

#### Scenario: Single-frame decode without MemoryPool rent
- **WHEN** a `WireBuffer` containing a complete DATA frame is passed to `Decode()`
- **THEN** the decoder SHALL parse the frame without calling `MemoryPool<byte>.Shared.Rent`
- **THEN** the returned frame SHALL reference memory from the adopted WireBuffer

#### Scenario: Multi-frame decode reuses frames list
- **WHEN** a `WireBuffer` containing multiple complete frames is passed to `Decode()`
- **THEN** the decoder SHALL return frames via a reusable `_frames` list (Clear+Repopulate)
- **THEN** the same list instance SHALL be returned on consecutive `Decode()` calls

#### Scenario: Previous working buffer disposed on next Decode
- **WHEN** `Decode()` is called with a new WireBuffer
- **THEN** the previously adopted `_workingBuffer` SHALL be disposed before adopting the new one

### Requirement: H3 FrameDecoder handles remainder without per-frame allocation
When an incomplete frame spans two `Decode()` calls, the decoder SHALL use a field-level remainder buffer instead of renting a new `IMemoryOwner<byte>` per frame.

#### Scenario: Incomplete frame across two buffers
- **WHEN** a `WireBuffer` ends mid-frame (e.g., partial HEADERS payload)
- **AND** the next `Decode()` call provides the remaining bytes
- **THEN** the decoder SHALL join the remainder with the new buffer using a field-level buffer
- **THEN** no `MemoryPool<byte>.Shared.Rent` SHALL be called

#### Scenario: Remainder buffer reused across multiple partial frames
- **WHEN** multiple consecutive frames arrive split across buffer boundaries
- **THEN** the same field-level remainder buffer SHALL be reused (grown if needed, never shrunk)

### Requirement: H3 FrameDecoder cleans up on pool return
Since H3 FrameDecoder is pooled per-stream via `ConnectionObjectPool`, `OnReset()` MUST dispose all held buffers.

#### Scenario: OnReset disposes adopted buffers
- **WHEN** the FrameDecoder is returned to the pool via `OnReset()`
- **THEN** `_workingBuffer` SHALL be disposed
- **THEN** `_remainderBuffer` SHALL be disposed
- **THEN** `_frames` list SHALL be cleared

#### Scenario: No buffer leak on stream close without pool return
- **WHEN** the FrameDecoder is disposed directly (e.g., stream error without pool return)
- **THEN** all held buffers SHALL be disposed

### Requirement: QpackInstructionDecoder adopts buffer without allocation
`QpackInstructionDecoder` SHALL use field-level remainder buffers instead of `MemoryPool<byte>.Shared.Rent` for incomplete instruction handling. The same adoption pattern as FrameDecoder applies.

#### Scenario: Complete instruction decoded without MemoryPool rent
- **WHEN** a buffer containing a complete QPACK instruction is decoded
- **THEN** no `MemoryPool<byte>.Shared.Rent` SHALL be called

#### Scenario: Partial instruction remainder uses field buffer
- **WHEN** an instruction is split across two decode calls
- **THEN** a field-level remainder buffer SHALL be used (not a fresh MemoryPool rent)

#### Scenario: Cleanup on dispose
- **WHEN** the QpackInstructionDecoder is disposed
- **THEN** all held remainder buffers SHALL be disposed

### Requirement: Functional parity with current implementation
All changes MUST preserve exact frame-parsing behavior. Frame boundaries, error detection, and remainder handling MUST produce identical results.

#### Scenario: All existing H3 FrameDecoder tests pass unchanged
- **WHEN** the existing H3 FrameDecoder test suite runs against the refactored implementation
- **THEN** all tests SHALL pass without modification (except tests that explicitly assert MemoryPool usage)

#### Scenario: All existing QpackInstructionDecoder tests pass unchanged
- **WHEN** the existing QpackInstructionDecoder test suite runs
- **THEN** all tests SHALL pass without modification

#### Scenario: Integration tests show no behavioral regression
- **WHEN** the full End2End integration test suite runs
- **THEN** all H3-related tests SHALL pass with identical results
