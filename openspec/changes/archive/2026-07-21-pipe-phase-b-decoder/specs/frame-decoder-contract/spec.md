## MODIFIED Requirements

### Requirement: Caller-owned buffer input

Both decoders SHALL accept `ReadOnlySequence<byte>` via `DecodeAll(in ReadOnlySequence<byte> input, out SequencePosition consumed)`. The caller retains ownership of the input buffer. The decoder SHALL NOT dispose, return, or otherwise manage the caller's memory.

#### Scenario: Caller disposes buffer after frame consumption
- **WHEN** a caller wraps a `WireBuffer.Memory` in `new ReadOnlySequence<byte>(memory)` and passes it to `DecodeAll`
- **AND** the caller disposes the underlying `WireBuffer` after iterating the returned frames
- **THEN** no use-after-free or data corruption SHALL occur

#### Scenario: consumed equals input end when all data is processed
- **WHEN** `DecodeAll` processes all bytes in the input sequence (no trailing partial frame)
- **THEN** `consumed` SHALL equal `input.End`

#### Scenario: consumed is less than input end when a partial frame remains
- **WHEN** the input sequence ends mid-frame (incomplete frame header or body)
- **THEN** `consumed` SHALL equal the position after the last fully decoded frame
- **THEN** the decoder SHALL NOT copy or otherwise retain the unconsumed trailing bytes internally

---

### Requirement: Zero-copy frame payloads for complete frames

Frame payloads fully contained within a single segment of the input `ReadOnlySequence<byte>` SHALL be zero-copy slices of that segment. No copy is made. Frame payloads spanning multiple segments are decoded via `SequenceReader<byte>` and MAY require a copy to assemble contiguous data.

#### Scenario: Payload aliases the input buffer (single segment)
- **WHEN** a complete frame is decoded from a single-segment input sequence with no remainder involved
- **THEN** the frame's payload memory SHALL alias the input buffer
- **THEN** mutating the input buffer SHALL be visible through the frame's payload

#### Scenario: Multiple complete frames in one single-segment input
- **WHEN** multiple complete frames are present in a single-segment input sequence
- **THEN** each frame's payload SHALL be a distinct slice of the input buffer
- **THEN** no intermediate copies SHALL be made

#### Scenario: Multi-segment frame header parsing
- **WHEN** a frame's fixed header (9 bytes for H2; QUIC varint type+length for H3) spans two or more segments of the input sequence
- **THEN** the decoder SHALL use `SequenceReader<byte>` to read the header correctly across the segment boundary
- **THEN** the frame's payload SHALL remain zero-copy when it is itself contained in a single segment

#### Scenario: Multi-segment frame payload parsing
- **WHEN** a frame's payload spans two or more segments of the input sequence
- **THEN** the decoder SHALL assemble the payload correctly (copy permitted) using `SequenceReader<byte>` / `ReadOnlySequence<byte>.Slice`
- **THEN** the assembled payload SHALL contain the same bytes as an equivalent single-segment decode of the same logical byte stream

---

### Requirement: Remainder handling — no internal buffering

The decoder MUST NOT maintain internal remainder buffers (`_remainderBuffer`, `_remainder`, or equivalent field-level byte[] storage). When input ends mid-frame, the decoder reports the truncation point via `consumed` and performs no internal copy of the unconsumed bytes. Retention of unconsumed bytes across `DecodeAll` calls, if any, is the caller's responsibility.

#### Scenario: No internal remainder fields
- **WHEN** a `FrameDecoder` instance (H2 or H3) is inspected
- **THEN** it SHALL NOT have `_remainderBuffer`, `_remainder`, `_remainderOffset`, `_remainderLength`, or similar byte-buffering fields

#### Scenario: Partial frame is not silently retried from decoder state
- **WHEN** `DecodeAll` is called with input ending mid-frame
- **AND** the next `DecodeAll` call is made with an unrelated, independently-constructed `ReadOnlySequence<byte>`
- **THEN** the decoder SHALL NOT attempt to complete the earlier partial frame from any retained bytes
- **THEN** only cross-call *protocol* state (e.g. H2 `_awaitingContinuationStreamId`) SHALL carry over, not byte-level remainder

#### Scenario: Protocol state still carries across calls
- **WHEN** a HEADERS frame without `END_HEADERS` is fully decoded in one `DecodeAll` call
- **AND** the matching CONTINUATION frame arrives, fully contained, in a subsequent `DecodeAll` call
- **THEN** the continuation state SHALL carry over correctly between calls (unaffected by remainder removal)

---

## REMOVED Requirements

### Requirement: Remainder handling with field-based byte[]
**Reason**: The decoder no longer owns partial-frame buffering. The `ReadOnlyMemory<byte>`-based contract that required the decoder to copy trailing bytes into a grow-on-demand `byte[]` is replaced by a `ReadOnlySequence<byte>`-based contract where the decoder reports a `SequencePosition` and retains no bytes itself. (Full cross-call byte retention is expected to be restored at the transport layer via `PipeReader.AdvanceTo(consumed, examined)` in a later, separate change — see `pipe-transport-tcp`. This spec does not depend on that change landing.)
**Migration**: Remove `_remainderBuffer`/`_remainderOffset`/`_remainderLength` (H2) and equivalent H3 fields. Change `DecodeAll` from `(ReadOnlyMemory<byte> input, out int bytesConsumed)` to `(in ReadOnlySequence<byte> input, out SequencePosition consumed)`. Callers wrap existing buffers with `new ReadOnlySequence<byte>(memory)`.
