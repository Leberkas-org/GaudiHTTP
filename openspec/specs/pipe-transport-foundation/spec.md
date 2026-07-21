# Pipe Transport Foundation

`IConnectionTransport` is the sole data path for TCP connections. The legacy fallback machinery (`Channel<WireBuffer>` outbound queue, `ReceiveAsync(WireBuffer)` inbound loop, `ReadEventState`, `TransportDataFlushed` credit system, `SendFlushed`/`OnFlushed` callbacks) has been removed. Callers on the TCP path MUST use `IConnectionTransport` exclusively; no TCP code path may construct or consume `WireBuffer`, `TransportData`, or `TransportDataFlushed`. QUIC/H3 paths are unaffected.

## Requirements

### Requirement: IConnectionTransport is the sole TCP data path
`IConnectionTransport` is the only data path for TCP connections -- the legacy fallback machinery
has been deleted. Callers on the TCP path MUST use `IConnectionTransport` exclusively; no TCP code
path may construct or consume `WireBuffer`, `TransportData`, or `TransportDataFlushed`.

#### Scenario: No TCP call site constructs WireBuffer
- **WHEN** a TCP connection sends or receives data
- **THEN** the send/receive path MUST go through `IConnectionTransport.GetMemory`/`Advance`/`FlushAsync`
  (outbound) or `IConnectionTransport.ReadAsync`/`AdvanceTo` (inbound)
- **AND** no TCP-path code MUST call `WireBuffer.Rent`

#### Scenario: No TCP call site dispatches TransportData
- **WHEN** the TCP transport stage reports a new connection or data event
- **THEN** the network port between transport stage and protocol stage MUST carry only
  `TransportConnected(IConnectionTransport)` and `TransportDisconnected(reason)`
- **AND** `TransportData`/`TransportDataFlushed` MUST NOT be created or dispatched on the TCP path

#### Scenario: WireBuffer and TransportData remain available for QUIC
- **WHEN** a QUIC/H3 connection sends or receives data
- **THEN** `WireBuffer` and `TransportData` remain fully functional and unmodified for the QUIC path
- **AND** no behavior described in this requirement applies to QUIC/H3

---

### Requirement: IConnectionTransport interface wraps Pipe operations

`IConnectionTransport` provides method-based access to an underlying `PipeReader` (inbound) and
`PipeWriter` (outbound) pair. Callers MUST NOT receive raw `PipeReader`/`PipeWriter` references — all
access goes through the interface's methods. The interface exposes: `ReadAsync`, `AdvanceTo` (two
overloads — consumed only, and consumed+examined), `GetMemory`, `Advance`, `FlushAsync`,
`CompleteOutput`, `Info`, and `Abort`. This requirement is identical to the one already specified in
`pipe-transport-tcp`; Phase A implements it in isolation, with no wiring into the live transport path.

#### Scenario: ReadAsync returns buffered data
- **WHEN** `ReadAsync(ct)` is called and data has previously been written into the input pipe (e.g. by a
  test writing directly through the paired `PipeWriter`)
- **THEN** the returned `ReadResult` contains a `ReadOnlySequence<byte>` with the buffered bytes
- **AND** the sequence remains valid until `AdvanceTo` is called

#### Scenario: AdvanceTo releases consumed pipe segments
- **WHEN** `AdvanceTo(consumed, examined)` is called after processing a `ReadResult`
- **THEN** pipe segments up to `consumed` are released back to the pipe's internal segment pool
- **AND** the next `ReadAsync` call returns bytes from `examined` onward plus any bytes written since

#### Scenario: GetMemory provides writable memory from the output pipe
- **WHEN** `GetMemory(sizeHint)` is called
- **THEN** a `Memory<byte>` of at least `sizeHint` bytes is returned, backed by the output pipe's buffer
- **AND** the memory remains valid until `Advance` or the next `GetMemory` call

#### Scenario: Advance commits bytes without making them flush-visible
- **WHEN** `Advance(bytes)` is called after writing into memory obtained from `GetMemory`
- **THEN** the specified number of bytes are committed to the output pipe
- **AND** those bytes are NOT yet observable by a reader of the output `PipeReader` until `FlushAsync` is
  called

#### Scenario: FlushAsync completes synchronously below the pause threshold
- **WHEN** `FlushAsync(ct)` is called and the output pipe's buffered (unread) bytes are below its
  configured `pauseWriterThreshold`
- **THEN** the returned `ValueTask<FlushResult>` completes synchronously
- **AND** the committed bytes become readable via the paired `PipeReader`

#### Scenario: FlushAsync completes asynchronously at or above the pause threshold
- **WHEN** `FlushAsync(ct)` is called and the output pipe's buffered bytes are at or above its configured
  `pauseWriterThreshold`
- **THEN** the returned `ValueTask<FlushResult>` does not complete synchronously
- **AND** it completes once the paired `PipeReader` side has advanced enough to drop the buffered amount
  below the configured `resumeWriterThreshold`

#### Scenario: CompleteOutput signals end of outbound data
- **WHEN** `CompleteOutput()` is called
- **THEN** the underlying output `PipeWriter` is completed
- **AND** a subsequent read from the paired `PipeReader` observes `IsCompleted = true` once all prior
  bytes are consumed

#### Scenario: Abort tears down both pipes
- **WHEN** `Abort()` is called
- **THEN** both the input and output pipes are completed with a cancellation/aborted exception
- **AND** any `ReadAsync`/`FlushAsync` call already in flight completes (via cancellation) rather than
  hanging indefinitely

#### Scenario: ReadAsync after input pipe completion returns completed
- **WHEN** the paired input `PipeWriter` has been completed (e.g. by a test simulating pump termination)
  and all buffered bytes have been consumed
- **THEN** the next `ReadAsync` returns a `ReadResult` with `IsCompleted = true`

#### Scenario: FlushAsync after output pipe completion returns completed
- **WHEN** the paired output `PipeReader` has been completed (e.g. by a test simulating pump termination)
- **THEN** the next `FlushAsync` returns a `FlushResult` with `IsCompleted = true`

---

### Requirement: Read pump fills a PipeWriter from a socket

A static `RunReadPump(Socket socket, PipeWriter writer, AdaptiveHint hint, CancellationToken ct)` method
reads from a socket and writes into a `PipeWriter`, using the existing `AdaptiveHint` sizing pattern to
size each receive. It is a free-standing background task, testable without a live `ConnectionTransport`
or transport stage — a loopback socket pair (or an equivalent test double) and a real `Pipe` are
sufficient.

#### Scenario: Socket data is written into the pipe
- **WHEN** `socket.ReceiveAsync` returns N > 0 bytes into memory obtained via `writer.GetMemory(hint)`
- **THEN** the pump calls `writer.Advance(N)` followed by `writer.FlushAsync(ct)`
- **AND** those bytes become readable via the paired `PipeReader.ReadAsync()`

#### Scenario: Socket EOF terminates the read pump cleanly
- **WHEN** `socket.ReceiveAsync` returns 0 bytes
- **THEN** the pump calls `writer.Complete()` with no exception and returns
- **AND** the paired `PipeReader`'s next `ReadAsync` reports `IsCompleted = true`, exposing any bytes
  written before EOF

#### Scenario: Socket error terminates the read pump with a fault
- **WHEN** `socket.ReceiveAsync` throws
- **THEN** the pump calls `writer.Complete(exception)` and returns without rethrowing out of the pump task
  (the pump's `Task` itself completes; it does not fault the caller synchronously)
- **AND** the paired `PipeReader`'s next `ReadAsync` reports `IsCompleted = true`

#### Scenario: Cancellation terminates the read pump
- **WHEN** the supplied `CancellationToken` is cancelled while the pump is awaiting `ReceiveAsync` or
  `FlushAsync`
- **THEN** the pump completes the `PipeWriter` (with or without an `OperationCanceledException`, per
  standard cancellation semantics) and its `Task` completes

#### Scenario: Inbound backpressure pauses the read pump
- **WHEN** the pipe's buffered (unread) bytes reach its configured `pauseWriterThreshold` because the
  consumer of the paired `PipeReader` is not calling `AdvanceTo`
- **THEN** the pump's `writer.FlushAsync(ct)` call does not complete synchronously, pausing the pump's
  receive loop
- **AND** the pump resumes (the flush completes) once the consumer calls `AdvanceTo` enough to drop
  buffered bytes below `resumeWriterThreshold`

#### Scenario: Adaptive hint grows and shrinks with observed read sizes
- **WHEN** consecutive `ReceiveAsync` calls return byte counts consistent with `AdaptiveHint.Adapt`'s
  grow/shrink rules (see `Servus.Akka.Transport.AdaptiveHint`)
- **THEN** the pump's next `GetMemory` size hint reflects the adapted value

---

### Requirement: Write pump drains a PipeReader to a socket

A static `RunWritePump(PipeReader reader, Socket socket, CancellationToken ct)` method reads buffered
segments from a `PipeReader` and sends each via the socket, then advances the reader. Like the read pump,
it is free-standing and unit-testable with a real `Pipe` and a loopback/test-double socket.

#### Scenario: Pipe data is sent to the socket
- **WHEN** `reader.ReadAsync(ct)` returns a non-empty `ReadOnlySequence<byte>` (written by a test via the
  paired `PipeWriter`, or in the live path via `IConnectionTransport.GetMemory`/`Advance`/`FlushAsync`)
- **THEN** the pump sends every segment of the sequence via `socket.SendAsync`, in order
- **AND** calls `reader.AdvanceTo(result.Buffer.End)` after all segments are sent

#### Scenario: Completed empty read terminates the write pump cleanly
- **WHEN** `reader.ReadAsync(ct)` returns a result with `IsCompleted = true` and an empty `Buffer`
- **THEN** the pump calls `reader.Complete()` and returns without sending anything further

#### Scenario: Producer completes the pipe after writing remaining data
- **WHEN** the paired `PipeWriter` is completed (e.g. via `IConnectionTransport.CompleteOutput()`) after
  some unsent bytes remain buffered
- **THEN** the pump drains and sends the remaining buffered bytes before observing the completed+empty
  state and terminating

#### Scenario: Socket error terminates the write pump with a fault
- **WHEN** `socket.SendAsync` throws
- **THEN** the pump calls `reader.Complete(exception)` and its `Task` completes without rethrowing
  synchronously to a caller awaiting the pump inline
- **AND** the paired `PipeWriter`'s next `FlushAsync` reports `IsCompleted = true`

#### Scenario: Cancellation terminates the write pump
- **WHEN** the supplied `CancellationToken` is cancelled while the pump is awaiting `ReadAsync` or
  `SendAsync`
- **THEN** the pump completes the `PipeReader` and its `Task` completes

---

### Requirement: TransportConnected can optionally carry an IConnectionTransport

`TransportConnected` gains an additive, optional `IConnectionTransport? Transport` member alongside the
existing `ConnectionInfo Info` member. Existing call sites that construct `TransportConnected` with only
`Info` continue to compile and behave identically — `Transport` defaults to `null`. No consumer reads this
member in Phase A; it exists so later phases (transport-stage wiring) have a landing spot without another
breaking signature change.

#### Scenario: Existing single-argument construction is unaffected
- **WHEN** `TransportConnected` is constructed as `new TransportConnected(info)`, as all current call
  sites do
- **THEN** it compiles unchanged and `Transport` is `null`

#### Scenario: Transport can be supplied when constructed with both arguments
- **WHEN** `TransportConnected` is constructed as `new TransportConnected(info, transport)` with a non-null
  `IConnectionTransport`
- **THEN** the resulting instance's `Transport` property returns that same instance

---

### Requirement: Pipe thresholds are configurable via transport options

The `pauseWriterThreshold` / `resumeWriterThreshold` used to construct the input and output pipes are
configurable, not hard-coded, with defaults matching the master plan: input pipe 128KB pause / 64KB
resume, output pipe 256KB pause / 128KB resume. Segment size is also configurable.

#### Scenario: Default thresholds apply when unconfigured
- **WHEN** pipe threshold options are not explicitly set
- **THEN** the input pipe is constructed with `pauseWriterThreshold = 128 * 1024` and
  `resumeWriterThreshold = 64 * 1024`
- **AND** the output pipe is constructed with `pauseWriterThreshold = 256 * 1024` and
  `resumeWriterThreshold = 128 * 1024`

#### Scenario: Custom thresholds are honored
- **WHEN** pipe threshold options specify custom pause/resume values for either pipe
- **THEN** the corresponding `PipeOptions` used to construct that pipe reflect the configured values
  instead of the defaults

#### Scenario: Custom segment size is honored
- **WHEN** a custom minimum segment size is configured
- **THEN** the `PipeOptions.MinimumSegmentSize` used for pipe construction reflects the configured value
