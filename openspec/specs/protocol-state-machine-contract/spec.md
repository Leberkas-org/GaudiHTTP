## Requirements

### Requirement: Construction requires stage operations callback
State machines that operate over TCP MUST extend `TcpStateMachineBase<TOps>`, which owns transport
lifecycle dispatch, the read loop, flush tracking, and async result routing. The concrete SM
provides protocol-specific callbacks (decode, connect/disconnect handling, flush completion) and
implements `IClientStateMachine` or `IServerStateMachine`. The operations interface MUST
expose `IActorRef Self` for `PipeTo` bridging, in addition to its existing members. The operations callback
is the sole channel through which the state machine communicates outward. A state machine MUST NOT capture
or use any Akka infrastructure directly beyond `ops.Self` for `PipeTo`.

#### Scenario: Client state machine construction (updated)
- **WHEN** an `IClientStateMachine` for a TCP protocol is constructed
- **THEN** it MUST extend `TcpStateMachineBase<IClientStageOperations>`
- **AND** it MUST pass the `IClientStageOperations` instance to the base constructor
- **AND** it MUST NOT create a standalone `TransportIo` instance

#### Scenario: Server state machine construction (updated)
- **WHEN** an `IServerStateMachine` for a TCP protocol is constructed
- **THEN** it MUST extend `TcpStateMachineBase<IServerStageOperations>`
- **AND** it MUST pass the `IServerStageOperations` instance to the base constructor

#### Scenario: Operations interface exposes Self
- **WHEN** the SM (in pipe mode) needs to bridge an async result to the actor thread
- **THEN** `_ops.Self` MUST return the StageActor's `IActorRef`
- **AND** the SM calls `PipeTo(_ops.Self, ...)` directly

---

### Requirement: Lifecycle follows PreStart, active processing, Cleanup
The state machine lifecycle is: construction (inert) -> `PreStart()` (initialize timers, send connection preface) -> active processing (decode/encode loop) -> `Cleanup()` (release all resources). `PreStart` is called exactly once after the stage materializes. `Cleanup` is called exactly once when the connection stage tears down, regardless of whether teardown is graceful or due to error.

#### Scenario: PreStart initializes connection-level state
- **WHEN** `PreStart()` is called on a state machine
- **THEN** the state machine MAY schedule initial timers (e.g., keep-alive, request-headers timeout)
- **THEN** the state machine MAY emit a connection preface (e.g., HTTP/2 client preface)
- **THEN** the state machine MUST be ready to accept inbound data after `PreStart` returns

#### Scenario: Cleanup releases all held resources
- **WHEN** `Cleanup()` is called
- **THEN** the state machine MUST cancel all active timers
- **THEN** the state machine MUST dispose all held buffers and memory owners
- **THEN** the state machine MUST fail any in-flight requests (client) or pending responses (server) that have not yet completed
- **THEN** the state machine MUST NOT call operations methods after `Cleanup` returns

#### Scenario: Cleanup is safe to call multiple times
- **WHEN** `Cleanup()` is called on an already-cleaned-up state machine
- **THEN** it MUST NOT throw

---

### Requirement: Inbound data arrives via DecodeServerData / DecodeClientData
For TCP protocols (H1.0, H1.1, H2), `DecodeServerData`/`DecodeClientData` MUST only dispatch
lifecycle events (`TransportConnected`, `TransportDisconnected`) via `DispatchLifecycleEvent`.
Byte data MUST NOT arrive through these methods for TCP protocols -- all byte data is delivered
by `TransportIo.ProcessReadResult` -> `DecodeData(ReadOnlySequence<byte>)` via the pipe read loop.

If a `TransportData` item is received on a TCP SM's `DecodeServerData`/`DecodeClientData` when
`Transport` is null, the SM MUST throw `InvalidOperationException` -- this indicates a programming
error (data arrived before transport was connected). The pre-connect `TransportData` fallback
path is removed.

For QUIC/H3 protocols, `DecodeClientData`/`DecodeServerData` is unchanged (still receives
`MultiplexedData` via port; H3 is out of scope for pipe transport).

#### Scenario: TransportConnected routed through base class
- **WHEN** `TransportConnected` is received in `DecodeServerData` / `DecodeClientData`
- **THEN** the SM MUST call the base class lifecycle dispatch
- **THEN** the base class MUST initialize the transport, start the read loop, and call
  `OnTransportConnected`

#### Scenario: TransportDisconnected triggers teardown or reconnect
- **WHEN** `TransportDisconnected` is received via the port dispatch
- **THEN** the state machine MUST set `_transport = null`
- **THEN** the state machine MUST increment `_transportGen` to invalidate stale PipeTo messages
- **THEN** if in-flight requests exist and reconnect policy allows, the state machine MAY enter
  reconnecting state
- **THEN** if no reconnect is possible, the state machine MUST fail all in-flight requests

#### Scenario: TransportData on TCP SM throws
- **WHEN** `TransportData` is received on a TCP SM's `DecodeServerData`/`DecodeClientData`
- **THEN** the SM MUST throw `InvalidOperationException`
- **AND** the message MUST indicate that TransportData is not supported on the pipe-transport path

#### Scenario: Byte data is decoded via DecodeData (pipe mode)
- **WHEN** the SM's read loop delivers a `ReadResult` (via sync fast-path or PipeTo dispatch) with
  `_transport != null`
- **THEN** the SM MUST call `DecodeData(ReadOnlySequence<byte>)` to process the bytes synchronously
- **THEN** the SM MUST call `_transport.AdvanceTo(consumed, examined)` after decode
- **THEN** the returned `consumed` position indicates how many bytes were fully processed

---

### Requirement: Outbound messages are submitted via OnRequest / OnResponse
Client state machines receive outbound work via `OnRequest(HttpRequestMessage)`. Server state machines
receive outbound work via `OnResponse(IFeatureCollection)`. The state machine encodes directly into
`Transport` via `GetMemory`/`Advance` and calls `RequestFlush()`; it MUST NOT call
`ops.OnOutbound(TransportData)` for byte data. `Transport` MUST NOT be null when encoding runs
(see `deferred-request-encoding` spec for the deferral mechanism).

#### Scenario: Client encodes a request onto the wire
- **WHEN** `OnRequest(HttpRequestMessage)` is called with `Transport` connected
- **THEN** the state machine MUST encode the request headers directly into `Transport` via
  `GetMemory`/`Advance`
- **THEN** the state machine MUST call `RequestFlush()` to trigger `FlushAsync`
- **THEN** if the request has a body, the state machine MUST arrange for body data to be pumped to
  `Transport`

#### Scenario: Server encodes a response onto the wire
- **WHEN** `OnResponse(IFeatureCollection)` is called with `Transport` connected
- **THEN** the state machine MUST encode the response headers directly into `Transport` via
  `GetMemory`/`Advance` and call `RequestFlush()`

#### Scenario: OnRequest is only called when CanAcceptRequest is true
- **WHEN** `CanAcceptRequest` is false on a client state machine
- **THEN** the connection stage MUST NOT call `OnRequest`
- **THEN** if called regardless, the state machine behavior is undefined (this is a caller contract
  violation) -- unchanged by transport mode

#### Scenario: OnResponse is only called when CanAcceptResponse is true
- **WHEN** `CanAcceptResponse` is false on a server state machine
- **THEN** the connection stage MUST NOT call `OnResponse` -- unchanged by transport mode

---

### Requirement: CanAcceptRequest / CanAcceptResponse gates admission
Client state machines expose `bool CanAcceptRequest` to indicate whether they can accept another request. This MUST return false when: the connection is reconnecting, the peer has sent a GOAWAY (or Connection: close), or protocol-specific limits are reached (e.g., max concurrent streams). Server state machines expose `bool CanAcceptResponse` to indicate whether a response can be encoded. This MUST return false when an outbound body is still being pumped or no pending request awaits a response.

#### Scenario: Client rejects requests during reconnect
- **WHEN** the client state machine is in a reconnecting state (`IsReconnecting` is true)
- **THEN** `CanAcceptRequest` MUST return false

#### Scenario: Client rejects requests after GOAWAY / Connection: close
- **WHEN** the peer has signaled that no new requests should be sent
- **THEN** `CanAcceptRequest` MUST return false

#### Scenario: Server rejects responses when body pump is active
- **WHEN** the server state machine is currently pumping a response body
- **THEN** `CanAcceptResponse` MUST return false

---

### Requirement: HasInFlightRequests tracks active client requests
Client state machines MUST expose `bool HasInFlightRequests` that is true whenever at least one request has been submitted but its response has not been fully received (headers + body complete). This property drives reconnect and graceful-shutdown decisions.

#### Scenario: No in-flight requests on fresh connection
- **WHEN** a client state machine has just been constructed and `PreStart` called
- **THEN** `HasInFlightRequests` MUST return false

#### Scenario: In-flight request tracked from submit to response completion
- **WHEN** `OnRequest` is called
- **THEN** `HasInFlightRequests` MUST return true
- **WHEN** the full response (headers + body) for that request is received and emitted
- **THEN** `HasInFlightRequests` MUST return false (if no other requests are pending)

---

### Requirement: Timer management via OnTimerFired
State machines use timers for protocol timeouts (keep-alive, request-headers, body-read, data-rate). Timers are scheduled via `ops.OnScheduleTimer(name, duration)` and cancelled via `ops.OnCancelTimer(name)`. When a timer fires, the connection stage calls `OnTimerFired(string name)` on the state machine. Timer names are state-machine-internal constants; the stage does not interpret them.

#### Scenario: Timer fires and state machine handles it
- **WHEN** `OnTimerFired(name)` is called
- **THEN** the state machine MUST handle the named timer (e.g., close idle connection, abort slow request)
- **THEN** the state machine MUST NOT throw for an unknown timer name (timers may be cancelled after firing due to actor message ordering)

#### Scenario: Cleanup cancels all active timers
- **WHEN** `Cleanup()` is called while timers are active
- **THEN** the state machine MUST call `ops.OnCancelTimer` for every timer it has scheduled

---

### Requirement: Body message coordination via OnBodyMessage
Body data (from outbound body pumps) arrives as typed messages via `OnBodyMessage(object msg)`.
`OnBodyMessage(object msg)` MUST call the base class's `OnAsyncResult(msg)` first. If the base
class handles the message (returns true), the SM MUST NOT process it further. Only unhandled
messages (body pump events, SM-specific messages) are processed by the SM. When `_transport != null`,
the state machine MUST encode the chunk directly into `_transport` via `GetMemory`/`Advance` and MUST call
`RequestFlush()`.

#### Scenario: ReadCompleted/FlushCompleted handled by base
- **WHEN** `OnBodyMessage` receives a `ReadCompleted` or `FlushCompleted` message
- **THEN** the base class `OnAsyncResult` MUST handle it and return true
- **AND** the SM MUST NOT process it further

#### Scenario: Body pump delivers chunks to the state machine (pipe mode)
- **WHEN** the body pump produces a data chunk and `_transport != null`
- **THEN** it is delivered to the state machine via `OnBodyMessage`
- **THEN** the state machine MUST encode and write the chunk directly into `_transport`
- **THEN** the state machine MUST call `RequestFlush()`

#### Scenario: Body pump signals completion
- **WHEN** the body pump signals that all body data has been sent
- **THEN** the state machine MUST emit the end-of-body marker on the wire (e.g., zero-length chunk for
  chunked encoding, END_STREAM flag for H2/H3) via `_transport`
- **THEN** the state machine MUST call `RequestFlush()` after emitting the marker

---

### Requirement: ShouldPauseNetwork controls transport back-pressure
Both client and server state machines expose `bool ShouldPauseNetwork` (defaulting to false). When
`_transport == null`, and this is true, the connection stage MUST stop pulling data from the transport
inlet, unchanged from prior behavior. When `_transport != null`, and this is true, the SM's
`RequestRead()` method MUST NOT call `ReadAsync()`.

#### Scenario: Body reader is full, network is paused (legacy)
- **WHEN** the inbound body reader's buffer is full and `_transport == null`
- **THEN** `ShouldPauseNetwork` MUST return true
- **THEN** the connection stage MUST NOT pull from the transport until the flag clears

#### Scenario: Body reader is full, reads are paused (pipe mode)
- **WHEN** the inbound body reader's buffer is full and `_transport != null`
- **THEN** `ShouldPauseNetwork` MUST return true
- **THEN** the SM's `RequestRead()` MUST be a no-op until the flag clears

#### Scenario: Body reader drains, reading resumes
- **WHEN** the application consumes body data and the reader has capacity again
- **THEN** `ShouldPauseNetwork` MUST return false
- **THEN** the connection stage resumes pulling (legacy) or the SM calls `RequestRead()` to re-arm (pipe
  mode), depending on which mode is active for this connection

---

### Requirement: Server exposes ShouldComplete for graceful teardown
Server state machines expose `bool ShouldComplete` to signal that the connection stage should complete (close the connection). This becomes true when the protocol requires connection closure (e.g., HTTP/1.0 after every response, HTTP/1.1 after Connection: close, or protocol negotiation failure).

#### Scenario: HTTP/1.0 completes after single response
- **WHEN** an HTTP/1.0 server state machine finishes sending a response
- **THEN** `ShouldComplete` MUST return true

#### Scenario: HTTP/1.1 with Connection: close
- **WHEN** an HTTP/1.1 server state machine sends or receives a Connection: close header
- **THEN** `ShouldComplete` MUST return true after the final response is sent

#### Scenario: Protocol negotiation failure
- **WHEN** the `ProtocolNegotiatingStateMachine` cannot identify the protocol within bounds
- **THEN** `ShouldComplete` MUST return true (via `_sniffAborted`)

---

### Requirement: Server exposes MaxQueuedRequests and MaxConcurrentRequests
Server state machines MUST expose `int MaxQueuedRequests` to limit how many decoded requests can be queued before the state machine pauses decoding. Server state machines MUST expose `int MaxConcurrentRequests` (default `int.MaxValue`) to limit how many requests the connection stage dispatches to the application handler concurrently. HTTP/1.x MUST return 1 for `MaxConcurrentRequests` to enforce response ordering (RFC 9112 section 9.3.2). Multiplexed protocols (H2, H3) MAY leave it unbounded.

#### Scenario: HTTP/1.1 serializes handler dispatch
- **WHEN** an HTTP/1.1 server state machine is active
- **THEN** `MaxConcurrentRequests` MUST return 1

#### Scenario: HTTP/2 allows concurrent handler dispatch
- **WHEN** an HTTP/2 server state machine is active
- **THEN** `MaxConcurrentRequests` MAY return `int.MaxValue` (limited by SETTINGS_MAX_CONCURRENT_STREAMS)

---

### Requirement: Client exposes IsReconnecting for connection pool coordination
Client state machines that support reconnection (H1.1, H2, H3) MUST expose `bool IsReconnecting` that is true while a reconnect attempt is in progress. The connection pool uses this to avoid routing new requests to a connection that is re-establishing.

#### Scenario: Reconnect in progress
- **WHEN** the transport disconnects and the state machine starts a reconnect attempt
- **THEN** `IsReconnecting` MUST return true
- **THEN** `CanAcceptRequest` MUST return false

#### Scenario: Reconnect succeeds
- **WHEN** `TransportConnected` is received after a reconnect
- **THEN** `IsReconnecting` MUST return false
- **THEN** buffered in-flight requests MUST be replayed (with body rewind for seekable bodies)

#### Scenario: Reconnect exhausted
- **WHEN** all reconnect attempts are exhausted (max attempts reached)
- **THEN** `IsReconnecting` MUST return false
- **THEN** all buffered in-flight requests MUST be failed with an appropriate exception

---

### Requirement: Protocol errors are expressed as HttpProtocolException
State machines MUST throw `HttpProtocolException` (or its subtypes `ConnectionProtocolException`, `StreamProtocolException`) for protocol violations detected during decoding. The connection stage catches these and disconnects the transport. For line-based protocols (H1.0, H1.1), all protocol errors are connection-fatal. For multiplexed protocols (H2, H3), stream-scoped errors (`StreamProtocolException`) reset the individual stream while connection-scoped errors (`ConnectionProtocolException`) tear down the entire connection.

#### Scenario: Connection-fatal protocol error
- **WHEN** a `ConnectionProtocolException` is thrown during `DecodeServerData` / `DecodeClientData`
- **THEN** the connection stage MUST disconnect the transport
- **THEN** the state machine MUST NOT process further data on this connection

#### Scenario: Stream-scoped protocol error (multiplexed only)
- **WHEN** a `StreamProtocolException` is thrown during frame processing
- **THEN** the state machine MUST reset the offending stream (RST_STREAM / stream reset)
- **THEN** the connection MUST remain open for other streams

---

### Requirement: OnUpstreamFinished / OnDownstreamFinished signals stage teardown
Client state machines implement `OnUpstreamFinished()` and server state machines implement `OnDownstreamFinished()` to handle the Akka Streams stage completing from the peer direction. This signals that no more data will arrive from the application layer (client: no more requests; server: no more responses).

#### Scenario: Client upstream finishes
- **WHEN** `OnUpstreamFinished()` is called on a client state machine
- **THEN** the state machine MAY allow in-flight requests to drain before closing
- **THEN** the state machine MUST NOT accept new requests

#### Scenario: Server downstream finishes
- **WHEN** `OnDownstreamFinished()` is called on a server state machine
- **THEN** the state machine MUST stop dispatching new requests to the application

---

### Requirement: OnRequestCancelled handles client-side cancellation
Client state machines MAY implement `OnRequestCancelled(HttpRequestMessage)` (default: no-op) to handle cancellation of a previously submitted request. For multiplexed protocols this SHOULD send a stream reset; for line-based protocols cancellation of an in-flight request is typically a no-op (the response must still be drained).

#### Scenario: Multiplexed request cancelled
- **WHEN** `OnRequestCancelled` is called for a request on H2/H3
- **THEN** the state machine SHOULD send a RST_STREAM / stream reset for the associated stream

#### Scenario: Line-based request cancelled
- **WHEN** `OnRequestCancelled` is called for a request on H1.0/H1.1
- **THEN** the state machine MAY ignore it (the response must be drained to maintain connection framing)

---

### Requirement: Server ResumeBody coordinates inbound body back-pressure
Server state machines MAY implement `ResumeBody()` (default: no-op) to handle the `BodyResumed` signal from the connection stage. This is called when the application has consumed enough body data to allow more inbound body bytes to be decoded. The connection stage intercepts `BodyResumed` messages and calls `ResumeBody()` before forwarding to `OnBodyMessage`.

#### Scenario: Body reader signals capacity available
- **WHEN** the application reads body data, freeing buffer space
- **THEN** the connection stage calls `ResumeBody()` on the state machine
- **THEN** the state machine MAY resume pulling network data or unblock a paused decode path

---

### Requirement: Threading model is single-threaded actor confinement
All state machine methods are called on the Akka Streams actor thread that owns the connection stage. State machines MUST NOT be accessed from multiple threads. No synchronization primitives (locks, volatile, Interlocked) are needed or permitted for state machine fields. The Akka message-passing model provides happens-before guarantees between method calls.

#### Scenario: All methods called on the actor thread
- **WHEN** any method on the state machine is called
- **THEN** it MUST be called on the owning actor thread (enforced by Akka Streams stage confinement)
- **THEN** the state machine MAY use plain field access without synchronization

#### Scenario: Body pump messages arrive via actor mailbox
- **WHEN** a body pump (which runs on a Task thread) produces a chunk
- **THEN** it MUST deliver it via `PipeTo` to the stage actor
- **THEN** the stage actor calls `OnBodyMessage` on the actor thread, preserving confinement

---

### Requirement: Protocol switch capability (server only)
Server state machines that support protocol upgrade (e.g., HTTP/1.1 -> HTTP/2 via h2c upgrade) operate under a `ProtocolNegotiatingStateMachine` wrapper. The wrapper implements `IServerStateMachine` and delegates to an inner state machine. When an upgrade is triggered, the wrapper calls `Cleanup()` on the old inner state machine, constructs the new one, and calls `PreStart()` -- the new state machine takes over the connection without the connection stage being aware of the switch.

#### Scenario: HTTP/1.1 to HTTP/2 upgrade
- **WHEN** an HTTP/1.1 server state machine detects an h2c upgrade request
- **THEN** it calls `RequestProtocolSwitch` on its `IProtocolSwitchCapable` operations wrapper
- **THEN** the old state machine is cleaned up and the new HTTP/2 state machine takes over
- **THEN** `PreStart()` is called on the new state machine

#### Scenario: Protocol negotiation sniffs cleartext bytes
- **WHEN** a cleartext connection arrives without ALPN
- **THEN** `ProtocolNegotiatingStateMachine` buffers initial bytes
- **THEN** it identifies the protocol (HTTP/2 preface, HTTP/1.0 version tag, or HTTP/1.1 request line)
- **THEN** it constructs the appropriate inner state machine and replays buffered data
