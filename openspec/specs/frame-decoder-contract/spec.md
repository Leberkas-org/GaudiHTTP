# Frame Decoder Contract

Specifies the shared contract for `FrameDecoder` in HTTP/2 (`Protocol.Syntax.Http2`) and HTTP/3 (`Protocol.Syntax.Http3`). Both decoders follow the same caller-owned-buffer, zero-copy, list-reuse pattern while differing in framing format, pooling model, and CONTINUATION state.

## Requirements

### Requirement: Caller-owned buffer input

Both decoders accept `ReadOnlyMemory<byte>` via `DecodeAll(ReadOnlyMemory<byte> input, out int bytesConsumed)`. The caller retains ownership of the input buffer. The decoder never disposes, returns, or otherwise manages the caller's memory.

#### Scenario: Caller disposes buffer after frame consumption
- **WHEN** a caller passes `ReadOnlyMemory<byte>` from a pooled buffer to `DecodeAll`
- **AND** the caller disposes the underlying buffer after iterating the returned frames
- **THEN** no use-after-free or data corruption SHALL occur

#### Scenario: bytesConsumed equals input length
- **WHEN** `DecodeAll` returns
- **THEN** `bytesConsumed` SHALL equal `input.Length` regardless of how many frames were decoded
- **THEN** unconsumed trailing bytes SHALL be stored internally as remainder, not left to the caller

---

### Requirement: Zero-copy frame payloads for complete frames

Frame payloads fully contained in a single `DecodeAll` input (no remainder involved) SHALL be zero-copy slices of the caller's `ReadOnlyMemory<byte>`. No copy is made.

#### Scenario: Payload aliases the input buffer
- **WHEN** a complete frame is decoded from input with no prior remainder
- **THEN** the frame's payload memory SHALL alias the input buffer
- **THEN** mutating the input buffer SHALL be visible through the frame's payload

#### Scenario: Multiple complete frames in one input
- **WHEN** multiple complete frames are present in a single input
- **THEN** each frame's payload SHALL be a distinct slice of the input buffer
- **THEN** no intermediate copies SHALL be made

---

### Requirement: Remainder handling with field-based byte[]

Incomplete trailing bytes from a `DecodeAll` call SHALL be stored in a field-level `byte[]` buffer. This buffer grows on demand but never shrinks. No `MemoryPool.Rent` or other external allocation mechanism is used for remainder storage.

#### Scenario: Partial frame buffered across two calls
- **WHEN** input ends mid-frame (fewer bytes than the frame header + payload require)
- **AND** the next `DecodeAll` call provides the remaining bytes
- **THEN** the complete frame SHALL be decoded from the combined data
- **THEN** no `MemoryPool.Rent` SHALL be called for remainder storage

#### Scenario: Remainder buffer grows on demand
- **WHEN** the remainder plus new input exceeds the current buffer capacity
- **THEN** a new, larger `byte[]` SHALL be allocated and the existing remainder copied into it
- **THEN** the old buffer is abandoned (no explicit return to any pool)

#### Scenario: Remainder buffer never shrinks
- **WHEN** a large remainder buffer was allocated for a previous decode cycle
- **AND** subsequent calls require less remainder space
- **THEN** the existing buffer SHALL be reused at its current size

#### Scenario: Remainder compaction before new slices
- **WHEN** remainder exists from a previous call (`_remainderOffset > 0`)
- **THEN** compaction (BlockCopy to offset 0) SHALL occur at the start of the next `DecodeAll` call
- **THEN** compaction completes before any new input is appended or frame slices reference the buffer

#### Scenario: Frame assembled from remainder owns its data
- **WHEN** a frame is assembled from remainder bytes + new input
- **THEN** the frame's payload SHALL reference the remainder buffer (not the caller's input)
- **THEN** mutating either original input SHALL NOT corrupt the frame's payload
- **THEN** the payload is valid only until the next `DecodeAll` call (remainder buffer is reused)

---

### Requirement: _frames list reuse

Each decoder maintains a single `List<TFrame>` field (`_frames`) that is reused across all `DecodeAll` calls. The same list instance is returned every time.

#### Scenario: Clear and repopulate on each call
- **WHEN** `DecodeAll` is called
- **THEN** `_frames.Clear()` SHALL be called before decoding begins
- **THEN** decoded frames SHALL be added to the same list instance

#### Scenario: Caller must not retain the list
- **WHEN** the caller receives the `IReadOnlyList<TFrame>` return value
- **THEN** the caller MUST fully consume the list before the next `DecodeAll` call
- **THEN** after the next `DecodeAll` call, the previously returned list reference contains different data

#### Scenario: Same instance across calls
- **WHEN** `DecodeAll` is called multiple times on the same decoder
- **THEN** `ReferenceEquals` on the returned lists SHALL be `true`

---

### Requirement: Thread confinement

Both decoders are designed for single-threaded use under Akka actor confinement. No field-level synchronization (locks, volatile, Interlocked) is used or required.

#### Scenario: Actor-thread-only access
- **WHEN** a decoder is used within an Akka Streams stage or state machine
- **THEN** all `DecodeAll`, `Reset`, and `Dispose`/`OnReset` calls SHALL occur on the same actor thread
- **THEN** no concurrent access SHALL occur

#### Scenario: Synchronous consumption within actor message
- **WHEN** `DecodeAll` returns a list of frames
- **THEN** the caller SHALL consume all frames synchronously within the same actor message processing
- **THEN** no frame or payload reference SHALL be retained across an `await` boundary or passed to another actor

---

### Requirement: Reset and Dispose contract

Both decoders support a reset path that releases all internal buffers and returns the decoder to its initial state.

#### Scenario: H2 Reset clears all state
- **WHEN** `Reset()` is called on the H2 `FrameDecoder`
- **THEN** `_remainderBuffer` SHALL be set to `null`
- **THEN** `_remainderOffset` and `_remainderLength` SHALL be set to 0
- **THEN** `_awaitingContinuationStreamId` SHALL be set to 0

#### Scenario: H2 Dispose clears buffers
- **WHEN** `Dispose()` is called on the H2 `FrameDecoder`
- **THEN** `_remainderBuffer` SHALL be set to `null`
- **THEN** `_remainderOffset` and `_remainderLength` SHALL be set to 0

#### Scenario: H3 OnReset clears all state for pooling
- **WHEN** the H3 `FrameDecoder` is returned to the `ConnectionObjectPool` and `OnReset()` is called
- **THEN** `_remainderBuffer` SHALL be set to `null`
- **THEN** `_remainderOffset` and `_remainderLength` SHALL be set to 0

#### Scenario: Decoder is reusable after Reset
- **WHEN** `Reset()` or `OnReset()` completes
- **THEN** the next `DecodeAll` call SHALL behave identically to a freshly constructed decoder

---

### Requirement: maxFrameSize validation (H2-specific)

The H2 `FrameDecoder` enforces SETTINGS_MAX_FRAME_SIZE (RFC 9113 section 4.2). The maximum is configurable via constructor parameter, defaulting to 16 MB - 1.

#### Scenario: Frame payload within limit
- **WHEN** a frame's 3-byte payload length field is less than or equal to `maxFrameSize`
- **THEN** decoding SHALL proceed normally

#### Scenario: Frame payload exceeds limit
- **WHEN** a frame's 3-byte payload length field exceeds `maxFrameSize`
- **THEN** `HttpProtocolException` SHALL be thrown immediately
- **THEN** the exception message SHALL reference RFC 9113 section 4.2

#### Scenario: SETTINGS_MAX_FRAME_SIZE range validation
- **WHEN** a SETTINGS frame contains a MAX_FRAME_SIZE value outside [16384, 16777215]
- **THEN** `HttpProtocolException` SHALL be thrown per RFC 9113 section 6.5.2

---

### Requirement: CONTINUATION state tracking (H2-specific)

The H2 `FrameDecoder` tracks whether a CONTINUATION frame sequence is in progress, enforcing RFC 9113 section 6.10 constraints across `DecodeAll` calls.

#### Scenario: HEADERS without END_HEADERS opens continuation state
- **WHEN** a HEADERS frame is decoded with `EndHeaders = false`
- **THEN** `_awaitingContinuationStreamId` SHALL be set to the frame's stream ID
- **THEN** the next frame MUST be a CONTINUATION on the same stream

#### Scenario: PUSH_PROMISE without END_HEADERS opens continuation state
- **WHEN** a PUSH_PROMISE frame is decoded with `EndHeaders = false`
- **THEN** `_awaitingContinuationStreamId` SHALL be set to the frame's stream ID

#### Scenario: CONTINUATION with END_HEADERS closes continuation state
- **WHEN** a CONTINUATION frame is decoded with `EndHeaders = true`
- **THEN** `_awaitingContinuationStreamId` SHALL be reset to 0

#### Scenario: Non-CONTINUATION frame during continuation is a protocol error
- **WHEN** `_awaitingContinuationStreamId` is non-zero
- **AND** the next frame is not a CONTINUATION
- **THEN** `HttpProtocolException` SHALL be thrown referencing RFC 9113 section 6.10

#### Scenario: CONTINUATION on wrong stream is a protocol error
- **WHEN** `_awaitingContinuationStreamId` is non-zero
- **AND** a CONTINUATION frame arrives on a different stream ID
- **THEN** `HttpProtocolException` SHALL be thrown referencing RFC 9113 section 6.10

#### Scenario: Unsolicited CONTINUATION is a protocol error
- **WHEN** `_awaitingContinuationStreamId` is 0 (no HEADERS/PUSH_PROMISE pending)
- **AND** a CONTINUATION frame is received
- **THEN** `HttpProtocolException` SHALL be thrown

#### Scenario: Continuation state survives across DecodeAll calls
- **WHEN** a HEADERS frame without END_HEADERS is decoded in one `DecodeAll` call
- **AND** the matching CONTINUATION frame arrives in a subsequent `DecodeAll` call
- **THEN** the continuation state SHALL carry over correctly between calls

---

### Requirement: Unknown frame type handling (H3-specific)

The H3 `FrameDecoder` gracefully skips unknown frame types per RFC 9114 section 7.2.8.

#### Scenario: Unknown frame type is skipped
- **WHEN** a frame with a type value not defined in `FrameType` is encountered
- **THEN** the decoder SHALL consume the frame's bytes (header + payload) without error
- **THEN** no frame SHALL be added to the `_frames` list for this input
- **THEN** decoding SHALL continue with the next frame

---

### Requirement: Variable-length integer framing (H3-specific)

The H3 `FrameDecoder` uses QUIC variable-length integer encoding (RFC 9000 section 16) for both frame type and payload length fields, unlike H2's fixed 9-byte header.

#### Scenario: Variable-length header decoded correctly
- **WHEN** a frame's type and length fields use different varint sizes (1, 2, 4, or 8 bytes each)
- **THEN** the decoder SHALL decode both fields correctly using `QuicVarInt.TryDecode`

#### Scenario: Partial varint triggers remainder buffering
- **WHEN** the input ends in the middle of a varint-encoded type or length field
- **THEN** the decoder SHALL buffer the partial bytes as remainder
- **THEN** the next `DecodeAll` call SHALL complete the varint decode

---

### Requirement: Pooling model difference

H2 and H3 decoders differ in lifecycle management. H2 uses `IDisposable` directly. H3 extends `Poolable<FrameDecoder>` for use with `ConnectionObjectPool`.

#### Scenario: H2 decoder is IDisposable
- **WHEN** an H2 connection is torn down
- **THEN** the H2 `FrameDecoder` SHALL be disposed via `IDisposable.Dispose()`
- **THEN** one decoder instance exists per H2 connection

#### Scenario: H3 decoder is poolable
- **WHEN** an H3 stream completes
- **THEN** the H3 `FrameDecoder` SHALL be returned to `ConnectionObjectPool` via `OnReset()`
- **THEN** the pool may reuse the instance for a subsequent stream
- **THEN** one decoder instance exists per H3 stream (not per connection)

---

## Shared vs. Protocol-Specific Summary

| Aspect | H2 | H3 |
|---|---|---|
| Input format | `ReadOnlyMemory<byte>` | `ReadOnlyMemory<byte>` |
| Frame header | Fixed 9 bytes | Variable (QUIC varint type + length) |
| `_frames` list reuse | Yes | Yes |
| Remainder buffer | Field `byte[]`, grow-on-demand | Field `byte[]`, grow-on-demand |
| Zero-copy payloads | Yes (complete frames) | Yes (complete frames) |
| CONTINUATION tracking | Yes (`_awaitingContinuationStreamId`) | N/A (H3 has no CONTINUATION) |
| maxFrameSize validation | Yes (constructor param) | N/A (QUIC stream framing handles this) |
| Unknown frame types | Returns `null`, not added to list | Skipped gracefully (RFC 9114 section 7.2.8) |
| Lifecycle | `IDisposable` (per-connection) | `Poolable<T>` (per-stream, pooled) |
| Reset clears continuation | Yes | N/A |
