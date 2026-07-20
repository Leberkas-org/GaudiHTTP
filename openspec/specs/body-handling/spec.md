# Body Handling Subsystem

The body handling subsystem manages HTTP message bodies on both the outbound (application-to-wire)
and inbound (wire-to-application) paths. It is the most bug-prone area of the codebase because it
sits at the intersection of buffer ownership, cross-thread signaling, and connection lifecycle.

## Requirements

### Requirement: Outbound pump selection

Each HTTP protocol version uses a specific pump type that matches its transport and flow-control
characteristics. The pump reads from the application's body `Stream` and delivers chunks to the
protocol encoder via the drain target.

- **SerialBodyPump** (H1.0, H1.1): single-stream, one body at a time per connection.
- **FlowControlledBodyPump** (H2): multi-stream with RFC 9113 flow-control windows (connection + per-stream).
- **MultiplexedBodyPump** (H3/QUIC): multi-stream with per-stream byte-credit backpressure (no protocol-level windows).

#### Scenario: Serial pump is used for H1.x connections
- **WHEN** a body is registered on an H1.0 or H1.1 connection
- **THEN** SerialBodyPump handles the drain (stream id is always 0)
- **AND** only one body can be active at a time

#### Scenario: Flow-controlled pump is used for H2 connections
- **WHEN** a body is registered on an H2 connection
- **THEN** FlowControlledBodyPump handles the drain
- **AND** reads are gated by both stream-level and connection-level send windows via FlowController

#### Scenario: Multiplexed pump is used for H3 connections
- **WHEN** a body is registered on an H3 connection
- **THEN** MultiplexedBodyPump handles the drain
- **AND** reads are gated by per-stream byte credit (AvailableBytes) replenished by transport flush acknowledgments

---

### Requirement: Force-async read discipline

All pumps deliver body-read completions through the Akka actor mailbox, never inline. This is a
benchmark-gated decision: inline processing regresses throughput 10-16% at high concurrency due to
fairness loss from long inline bursts on the actor thread.

#### Scenario: Synchronously completed reads are force-async'd
- **WHEN** a `Stream.ReadAsync` returns a `ValueTask` that is already completed (`IsCompletedSuccessfully`)
- **THEN** the pump wraps the result in a `BodyReadComplete` message and `Tell`s it to the stage actor
- **AND** does NOT process the result inline

#### Scenario: Truly async reads use PipeTo
- **WHEN** a `Stream.ReadAsync` returns a pending `ValueTask`
- **THEN** the pump uses `PipeTo` to route the completion to the stage actor mailbox
- **AND** the `CachedSuccessTransform`/`CachedFailureTransform` on PumpSlot avoid per-read closure allocations

---

### Requirement: Outbound byte-credit backpressure

Pumps gate reads against an outbound byte budget so that at most a bounded number of body bytes are
in flight ahead of the socket. Without this, a fast application stream can exhaust the shared pool.

#### Scenario: SerialBodyPump byte budget
- **WHEN** a serial pump is created with `maxBytes`
- **THEN** `_availableBytes` is initialized to `maxBytes` at Register
- **AND** each emitted chunk debits `_availableBytes` by the actual bytes read
- **AND** a `TransportDataFlushed` event credits bytes back via `OnCapacityAvailable`, clamped to `maxBytes`
- **AND** the pump does not start a new read while `_availableBytes <= 0`

#### Scenario: MultiplexedBodyPump per-stream budget
- **WHEN** a multiplexed pump registers a stream
- **THEN** the slot's `AvailableBytes` is initialized to `maxBytesPerStream`
- **AND** each emitted chunk debits `AvailableBytes` by the actual bytes read
- **AND** a `MultiplexedDataFlushed` event credits bytes back per-stream via `OnCapacityAvailable`
- **AND** a depleted budget parks ONLY that stream (via removing it from the ready queue), leaving sibling streams free to drain

#### Scenario: FlowControlledBodyPump window gating
- **WHEN** a flow-controlled pump attempts to schedule a read
- **THEN** it checks both the stream send window and the connection send window via FlowController
- **AND** if the available window is below `chunkSize / 2`, the stream is moved to `_windowBlockedStreams`
- **AND** the read size is `min(chunkSize, streamWindow, connectionWindow)`
- **AND** the window is reserved via `FlowController.Reserve` before the read starts
- **AND** any unused reservation (read returned fewer bytes) is refunded via `FlowController.Refund`

---

### Requirement: Pump concurrency limits

Multi-stream pumps limit the number of concurrent in-flight reads to avoid saturating the thread pool
and to bound the number of simultaneously rented pool buffers.

#### Scenario: MultiplexedBodyPump caps concurrent reads
- **WHEN** the pump has `maxConcurrentReads` reads in flight (default 4)
- **THEN** `TryScheduleReads` does not start additional reads until a completion lands

#### Scenario: FlowControlledBodyPump adaptive read slots
- **WHEN** the pump starts, it uses 2 read slots (`_readSlots = 2`)
- **AND** each successful read result increments `_readSlots` by 1, up to `hardCap`
- **THEN** `TryScheduleReads` respects `min(_readSlots, hardCap)` as the concurrency limit

---

### Requirement: Pump buffer ownership

Each pump rents a buffer from the wire buffer pool before starting a read. The buffer is owned by
the pump slot until the read completes and the data is handed to the drain target.

#### Scenario: SerialBodyPump buffer lifecycle
- **WHEN** a read is started
- **THEN** a `WireBuffer` is rented and stored in `_activeOwner`
- **AND** `Length` is set to `Capacity` (WireBuffer.Rent leaves Length at 0)
- **AND** after a successful read, the buffer is passed to `EmitOwnedDataFrames` which disposes it after emission
- **AND** on read failure or end-of-stream, the buffer is disposed by the pump

#### Scenario: Multi-stream pump buffer lifecycle (PumpSlot)
- **WHEN** a read is started for a PumpSlot
- **THEN** `EnsureBuffer` rents a `WireBuffer` if the slot does not already have one
- **AND** the buffer is reused across reads for the same stream (not re-rented per read)
- **AND** `DisposeResources` on the slot disposes the buffer when the stream completes, is cancelled, or is orphaned

#### Scenario: Buffer must not be recycled while a read is in flight
- **WHEN** a body read is cancelled or the connection tears down while a `ReadAsync` is still pending
- **THEN** the pump does NOT dispose the buffer immediately
- **AND** instead marks the slot as orphaned (multi-stream) or sets `_teardownPending` (serial)
- **AND** defers buffer disposal to the read-completion handler (`HandleReadComplete`/`HandleReadFailed`)
- **AND** this prevents a use-after-free where a recycled pool array is written to by the pending read

---

### Requirement: Pump cancellation

Body drains can be cancelled (e.g., request aborted, RST_STREAM) while a read may be in flight.
Cancellation must not corrupt shared state or leak resources.

#### Scenario: Cancel with no read in flight
- **WHEN** `Cancel` is called and no read is in flight
- **THEN** the slot is immediately released (resources disposed, slot returned to pool)

#### Scenario: Cancel with read in flight
- **WHEN** `Cancel` is called while a `ReadAsync` is pending
- **THEN** the linked CTS is cancelled (so the read observes cancellation)
- **AND** the slot is marked orphaned (`MarkOrphaned` / `_teardownPending`)
- **AND** cleanup is deferred to the read-completion handler
- **AND** the slot is removed from `_activeSlots` only when the completion handler runs (lazy removal from ready queue is safe because a missing slot is skipped at dequeue)

---

### Requirement: Pump reconnect (Cleanup) and generation guards

On connection reconnect (H2 GOAWAY, H3 connection migration), pumps must discard stale completions
from the old connection without corrupting streams on the new connection that reuse the same stream ids.

#### Scenario: Cleanup bumps generation and drains in-flight slots
- **WHEN** `Cleanup` is called on a multi-stream pump
- **THEN** the generation counter is incremented FIRST
- **AND** the old CTS is cancelled (forcing outstanding reads to complete)
- **AND** slots with in-flight reads are marked orphaned and moved to `_draining` (not released)
- **AND** slots without in-flight reads are released immediately
- **AND** `_activeSlots` is cleared (so replayed requests can re-register the same stream ids)
- **AND** a fresh CTS is created for the new incarnation

#### Scenario: Stale completion is dropped by generation guard
- **WHEN** a `BodyReadComplete` or `BodyReadFailed` arrives with a generation older than the pump's current generation
- **THEN** the pump releases the draining orphan (matching by generation + stream id)
- **AND** does NOT touch `_activeSlots` (a new request may already occupy that stream id)

#### Scenario: SerialBodyPump deferred teardown on Cleanup
- **WHEN** `Cleanup` is called on SerialBodyPump while a read is in flight
- **THEN** `_teardownPending` is set to true
- **AND** the caller cancels the connection CTS, which completes the outstanding read
- **AND** `FinishDeferredTeardown` runs in the completion handler, disposing the buffer and CTS

---

### Requirement: Drain target contract

Pumps communicate results to the protocol state machine through the `IBodyDrainTarget` (H1/H2) or
`IMultiplexedBodyDrainTarget` (H3) interface.

#### Scenario: Data emission
- **WHEN** a body chunk is read successfully (bytesRead > 0)
- **THEN** the pump calls `EmitDataFrames(streamId, data, endStream: false)` on the target
- **AND** for SerialBodyPump, it may call `EmitOwnedDataFrames` which transfers buffer ownership to the target

#### Scenario: End of stream
- **WHEN** a body read returns 0 bytes
- **THEN** the pump calls `EmitDataFrames(streamId, default, endStream: true)`
- **AND** then calls `OnDrainComplete(streamId)`

#### Scenario: Read failure
- **WHEN** a body read throws an exception (and the slot is not orphaned)
- **THEN** the pump calls `OnDrainFailed(streamId, reason)` on the target

---

### Requirement: Inbound reader selection

The body reader type is selected at stream-open time based on the response's framing and size.
The selection is performed by `BodyReaderPoolExtensions.RentBodyReader` using `BodyReaderClassification`.

#### Scenario: Small known-length bodies use BufferedBodyReader
- **WHEN** the response has a Content-Length
- **AND** the Content-Length is at or below `StreamingThreshold` AND at or below `MaxBufferedBodySize`
- **THEN** a `BufferedBodyReader` is rented and `Reset(contentLength)` is called
- **AND** no framing decoder is needed (data is fed directly)

#### Scenario: Large or chunked bodies use QueuedBodyReader
- **WHEN** the response is chunked-encoded, or has a Content-Length above the buffering threshold, or is close-delimited
- **THEN** a `QueuedBodyReader` is rented (capacity: 8 slots)
- **AND** the appropriate `IFramingDecoder` is rented alongside it

#### Scenario: No body
- **WHEN** the body classification is `BodyFraming.None`
- **THEN** `RentBodyReader` returns `(null, null)`

---

### Requirement: BufferedBodyReader contract

BufferedBodyReader accumulates the entire body in a single contiguous buffer. It is used only for
small, known-length bodies where buffering is cheaper than the streaming machinery.

#### Scenario: Fixed-length feeding
- **WHEN** `Reset(contentLength)` is called
- **THEN** a `WireBuffer` is rented with at least `contentLength` capacity (reused if already large enough)
- **AND** `Feed(data)` copies at most `_expected - _received` bytes
- **AND** `IsCompleted` becomes true when `_received == _expected`

#### Scenario: Open-ended feeding
- **WHEN** `ResetOpenEnded()` is called (close-delimited H1.0 bodies)
- **THEN** an initial 4 KB buffer is rented
- **AND** `Feed(data)` copies all bytes, growing the buffer as needed (doubling strategy)
- **AND** `MarkComplete()` must be called explicitly to set `IsCompleted`

#### Scenario: Body retrieval
- **WHEN** `GetBody()` is called
- **THEN** it returns `ReadOnlyMemory<byte>` over the received portion (`_owner.Memory[.._received]`)
- **AND** `AsOwningStream()` returns a `PooledMemoryStream` that takes ownership of the buffer and disposes it on stream close

#### Scenario: Buffer reuse across pooled lifetimes
- **WHEN** the reader is returned to the pool (`OnReset`)
- **THEN** buffers larger than 1 MB are disposed (prevents long-term large-buffer retention)
- **AND** buffers at or below 1 MB are retained for the next rental

#### Scenario: PooledMemoryStream double-dispose safety
- **WHEN** the owning stream is disposed multiple times (e.g., wrapping decompressor + consumer both dispose)
- **THEN** only the first Dispose releases the buffer (`Interlocked.Exchange`)
- **AND** subsequent Dispose calls are no-ops

---

### Requirement: QueuedBodyReader contract

QueuedBodyReader is the cross-thread boundary between the connection actor thread (producer) and the
application thread (consumer). All mutable state is guarded by `_sync`.

#### Scenario: Enqueue-dequeue flow
- **WHEN** the producer calls `TryEnqueue(data)` on the actor thread
- **THEN** a pooled byte array is rented from `WireBuffer.SharedPool`, data is copied in
- **AND** if a consumer is waiting (`_readPending`), the chunk is delivered directly via the VTS core (SetResult)
- **AND** if no consumer is waiting, the chunk is queued in the ring buffer
- **AND** the return value indicates whether the queue is below the backpressure threshold

#### Scenario: Consumer reads
- **WHEN** the consumer calls `ReadAsync` on the application thread
- **THEN** if chunks are queued, the head chunk is dequeued and returned synchronously (no VTS allocation)
- **AND** if the queue is empty and not completed/faulted, `_readPending` is set and a `ValueTask` backed by the VTS is returned
- **AND** the consumer must call `AdvanceTo()` after processing each chunk to return the rental to the pool and fire `SlotFreed`

#### Scenario: Backpressure signaling
- **WHEN** the queue reaches `_backpressureThreshold` (capacity) chunks
- **THEN** `TryEnqueue` returns `false` (IsFull is true)
- **AND** the producer (protocol state machine) pauses feeding until `SlotFreed` fires
- **AND** `SlotFreed` fires when `AdvanceTo()` is called by the consumer

#### Scenario: Completion
- **WHEN** `Complete()` is called by the producer
- **THEN** if a consumer is waiting (`_readPending`), it receives `BodyReadResult(default, isCompleted: true)`
- **AND** if no consumer is waiting, the flag is set and the next `ReadAsync` returns the completion synchronously

#### Scenario: Fault
- **WHEN** `Fault(ex)` is called by the producer
- **THEN** if a consumer is waiting, the VTS completes with the exception
- **AND** if no consumer is waiting, the exception is stored and the next `ReadAsync` throws it

#### Scenario: Cancellation of pending read
- **WHEN** the consumer's `CancellationToken` fires while a read is pending
- **THEN** `OnReadCancelled` checks the VTS version under `_sync` (guards against stale callbacks from pooled reuse)
- **AND** if the version matches and `_readPending` is true, completes the VTS with `OperationCanceledException`

---

### Requirement: QueuedBodyReader pool safety

QueuedBodyReader is `Poolable` and is recycled across unrelated requests. Stale callbacks from a
previous rental must not corrupt the new rental's state.

#### Scenario: OnReset drains queued chunks
- **WHEN** the reader is returned to the pool
- **THEN** all queued chunks have their rentals returned to the pool
- **AND** `_current`'s rental is NOT returned (it was published to the consumer and may still be read on another thread)
- **AND** the VTS core version is bumped (via `Reset()`) to invalidate stale cancel callbacks
- **AND** the cancel registration is disposed outside `_sync` to avoid deadlock
- **AND** if the slot array grew beyond initial size, it is replaced with a fresh initial-size array

#### Scenario: Stale cancel callback is rejected
- **WHEN** a cancel callback fires after the reader was recycled and re-rented
- **THEN** the version check in `OnReadCancelled` fails (core version was bumped)
- **AND** the callback is a no-op

#### Scenario: Pending read during reset
- **WHEN** `OnReset` is called while a consumer is awaiting a read
- **THEN** the pending read is completed with `BodyReadResult(default, isCompleted: true)` (not cancelled)
- **AND** the core is NOT reset before delivery (the consumer's captured version must still match)

---

### Requirement: QueuedBodyStream adapter

`QueuedBodyStream` wraps `QueuedBodyReader` as a `System.IO.Stream` for use by the application.

#### Scenario: Async read delegation
- **WHEN** `ReadAsync` is called on the stream
- **THEN** it calls `reader.ReadAsync` and copies from the result memory into the caller's buffer
- **AND** calls `reader.AdvanceTo()` only when the current chunk is fully consumed

#### Scenario: CopyToAsync zero-copy optimization
- **WHEN** `CopyToAsync` is called
- **THEN** it writes each chunk's `ReadOnlyMemory<byte>` directly to the destination stream (no intermediate buffer copy)
- **AND** calls `reader.AdvanceTo()` only AFTER the write completes (buffer must not be recycled while in use)

#### Scenario: Synchronous read guard
- **WHEN** `Read(Span<byte>)` is called
- **THEN** it attempts `reader.ReadAsync` with `CancellationToken.None`
- **AND** if the ValueTask is not completed synchronously, throws `InvalidOperationException`

#### Scenario: Abandonment notification
- **WHEN** the stream is disposed before the body is fully consumed (`_done` is false)
- **THEN** the `onAbandoned` callback is invoked (if provided)
- **AND** this allows the protocol layer to detect and handle abandoned bodies (e.g., drain or RST_STREAM)

---

### Requirement: Framing decoder contract

`IFramingDecoder` extracts body data from the raw wire bytes according to the HTTP transfer encoding.
It is a state machine that is fed raw bytes incrementally.

#### Scenario: ContentLengthFramingDecoder
- **WHEN** initialized with a content length N
- **THEN** `Decode` consumes up to N bytes from the input, returning them as body data
- **AND** `SupportsZeroCopy` is true (output slices directly from the input span)
- **AND** `IsComplete` becomes true when all N bytes have been consumed
- **AND** `OnEof` returns true only if the content length was fully consumed

#### Scenario: ChunkedFramingDecoder
- **WHEN** initialized for chunked transfer encoding
- **THEN** `Decode` parses chunk-size lines, chunk data, chunk-data CRLF, and trailers
- **AND** `SupportsZeroCopy` is false (output may come from the stash buffer for split chunks)
- **AND** `IsComplete` becomes true after the terminal `0\r\n\r\n` chunk
- **AND** parsed trailers are available via the `Trailers` property
- **AND** body size is enforced against `_maxBodySize`
- **AND** chunk extension length is enforced against `_maxChunkExtensionLength`
- **AND** trailer section size is enforced against `_maxTrailerSectionBytes`

#### Scenario: CloseDelimitedFramingDecoder
- **WHEN** initialized for close-delimited bodies (H1.0 without Content-Length)
- **THEN** `Decode` consumes all input bytes as body data
- **AND** `SupportsZeroCopy` is true
- **AND** `IsComplete` becomes true only when `OnEof` is called (connection close)
- **AND** body size is enforced against `_maxBodySize`

#### Scenario: Decoder Drain mode
- **WHEN** `Drain(raw)` is called
- **THEN** the decoder consumes bytes without producing output (discards body data)
- **AND** returns the number of raw bytes consumed
- **AND** this is used to skip unread body data on the wire (e.g., when the application ignores the body)

---

### Requirement: Framing decoder stash correctness

The `ChunkedFramingDecoder` must correctly handle chunk boundaries that are split across multiple
network reads. It uses an internal stash buffer for partial control lines.

#### Scenario: Split chunk-size line
- **WHEN** a chunk-size line is split across two `Decode` calls
- **THEN** the partial line is stashed
- **AND** the next `Decode` prepends the stash to the new input
- **AND** `rawConsumed` reports the entire raw input as consumed (including the stashed portion)
- **AND** the stashed bytes are NOT re-fed by the caller (they are owned by the decoder)

#### Scenario: Stash overflow protection
- **WHEN** the stash exceeds `max(maxControlLineLength, maxChunkExtensionLength)` without finding a CRLF
- **THEN** an `HttpProtocolException` is thrown

---

### Requirement: Reader and decoder pooling

All body readers and framing decoders extend `Poolable<T>` and are rented from `ConnectionObjectPool`.
This eliminates per-request allocations on the hot path.

#### Scenario: Rent and return lifecycle
- **WHEN** a body reader is needed
- **THEN** `BodyReaderPoolExtensions.RentBodyReader` rents the appropriate reader and decoder from `ConnectionObjectPool`
- **AND** `ReturnBodyReader` disposes both (which triggers `OnReset` and returns them to the pool)

#### Scenario: WireBuffer.SharedPool for QueuedBodyReader chunks
- **WHEN** QueuedBodyReader enqueues a chunk
- **THEN** it rents from `WireBuffer.SharedPool` (global locked stacks, no core affinity)
- **AND** not from `ArrayPool<byte>.Shared` (which has per-core buckets that miss on cross-thread return, causing 2-12x extra allocation)

---

### Requirement: Body reader classification

`BodyReaderClassification` determines which reader and decoder to use based on the response headers.

#### Scenario: Content-Length below threshold produces buffered reader
- **WHEN** `BodyFraming.Length` and `ContentLength <= StreamingThreshold` and `ContentLength <= MaxBufferedBodySize`
- **THEN** `IsBuffered` is true

#### Scenario: Content-Length above threshold produces streaming reader
- **WHEN** `BodyFraming.Length` and `ContentLength > StreamingThreshold` or `ContentLength > MaxBufferedBodySize`
- **THEN** `IsBuffered` is false, `HasContentLength` is true

#### Scenario: Content-Length exceeding hard limit is rejected
- **WHEN** `BodyFraming.Length` and `ContentLength > MaxStreamedBodySize`
- **THEN** an `HttpProtocolException` is thrown before any reader is rented

#### Scenario: Chunked framing
- **WHEN** `BodyFraming.Chunked`
- **THEN** `IsChunked` is true, `IsBuffered` is false (chunked bodies are always streamed)

#### Scenario: Close-delimited framing
- **WHEN** `BodyFraming.Close`
- **THEN** `HasBody` is true, `IsChunked` is false, `HasContentLength` is false, `IsBuffered` is false
