## MODIFIED Requirements

### Requirement: Inbound data arrives via DecodeServerData / DecodeClientData
For TCP protocols (H1.0, H1.1, H2), inbound byte data continues to arrive through
`DecodeServerData`/`DecodeClientData` as `TransportConnected`, `TransportData`, `TransportDisconnected` when
`_transport` is null (legacy path, still the only path production traffic exercises after this phase).
When `TransportConnected` carries a non-null `Transport`, the SM stores it and switches to pipe mode: byte
data is instead delivered by `RequestRead()`/`OnReadCompleted` and processed via a new `DecodeData(ReadOnlySequence<byte>)`
method (see `sm-transport-io`); `TransportData` items MUST NOT appear on the port for a connection that has
switched to pipe mode.

For QUIC/H3 protocols, `DecodeClientData`/`DecodeServerData` is unchanged (still receives `MultiplexedData`
via port; H3 is out of scope for this phase).

#### Scenario: TransportConnected without a transport keeps legacy decoding
- **WHEN** `TransportConnected` is received with `Transport == null`
- **THEN** the state machine MUST initialize (or re-initialize) its session state as before this change
- **THEN** subsequent `TransportData` items MUST be decoded synchronously as before this change

#### Scenario: TransportConnected with a transport switches to pipe mode
- **WHEN** `TransportConnected` is received with a non-null `Transport`
- **THEN** the state machine MUST store the `IConnectionTransport`
- **THEN** the state machine MUST initialize (or re-initialize) its session state
- **THEN** the state machine MUST start the read loop by calling `RequestRead()`
- **THEN** subsequent `TransportData` items on the port for this connection are not expected (nothing
  produces them in pipe mode) and MUST NOT be relied upon

#### Scenario: TransportDisconnected triggers teardown or reconnect
- **WHEN** `TransportDisconnected` is received via the port dispatch
- **THEN** the state machine MUST set `_transport = null`
- **THEN** the state machine MUST increment `_transportGen` to invalidate stale PipeTo messages, whether or
  not a transport was active
- **THEN** if in-flight requests exist and reconnect policy allows, the state machine MAY enter reconnecting
  state
- **THEN** if no reconnect is possible, the state machine MUST fail all in-flight requests

#### Scenario: TransportData is decoded synchronously (legacy path)
- **WHEN** `TransportData` is received with a `WireBuffer` and `_transport` is null
- **THEN** the state machine MUST decode the buffer synchronously within the same actor message, unchanged
  from prior behavior
- **THEN** the state machine MUST dispose (or adopt) the `WireBuffer` -- it MUST NOT let it leak

#### Scenario: Byte data is decoded via DecodeData (pipe mode)
- **WHEN** the SM's read loop delivers a `ReadResult` (via sync fast-path or PipeTo dispatch) with
  `_transport != null`
- **THEN** the SM MUST call `DecodeData(ReadOnlySequence<byte>)` to process the bytes synchronously
- **THEN** the SM MUST call `_transport.AdvanceTo(consumed, examined)` after decode
- **THEN** the returned `consumed` position indicates how many bytes were fully processed

---

### Requirement: Outbound messages are submitted via OnRequest / OnResponse
Client state machines receive outbound work via `OnRequest(HttpRequestMessage)`. Server state machines
receive outbound work via `OnResponse(IFeatureCollection)`. When `_transport` is null, the state machine
encodes the message and emits transport data via `ops.OnOutbound(TransportData)`, unchanged from prior
behavior. When `_transport != null`, the state machine encodes directly into `_transport` via
`GetMemory`/`Advance` (see `sm-transport-io`) and calls `RequestFlush()`; it MUST NOT call
`ops.OnOutbound(TransportData)` for byte data in this mode.

#### Scenario: Client encodes a request onto the wire (legacy)
- **WHEN** `OnRequest(HttpRequestMessage)` is called with `_transport == null`
- **THEN** the state machine MUST encode the request headers and emit them via `ops.OnOutbound`, unchanged
  from prior behavior
- **THEN** if the request has a body, the state machine MUST arrange for body data to be pumped to the
  transport via the same legacy mechanism

#### Scenario: Client encodes a request onto the wire (pipe mode)
- **WHEN** `OnRequest(HttpRequestMessage)` is called with `_transport != null`
- **THEN** the state machine MUST encode the request headers directly into `_transport` via
  `GetMemory`/`Advance`
- **THEN** the state machine MUST call `RequestFlush()` to trigger `FlushAsync`
- **THEN** if the request has a body, the state machine MUST arrange for body data to be pumped to
  `_transport`

#### Scenario: Server encodes a response onto the wire (legacy)
- **WHEN** `OnResponse(IFeatureCollection)` is called with `_transport == null`
- **THEN** the state machine MUST encode the response headers and emit them via `ops.OnOutbound`, unchanged
  from prior behavior

#### Scenario: Server encodes a response onto the wire (pipe mode)
- **WHEN** `OnResponse(IFeatureCollection)` is called with `_transport != null`
- **THEN** the state machine MUST encode the response headers directly into `_transport` via
  `GetMemory`/`Advance` and call `RequestFlush()`

#### Scenario: OnRequest is only called when CanAcceptRequest is true
- **WHEN** `CanAcceptRequest` is false on a client state machine
- **THEN** the connection stage MUST NOT call `OnRequest`
- **THEN** if called regardless, the state machine behavior is undefined (this is a caller contract
  violation) — unchanged by transport mode

#### Scenario: OnResponse is only called when CanAcceptResponse is true
- **WHEN** `CanAcceptResponse` is false on a server state machine
- **THEN** the connection stage MUST NOT call `OnResponse` — unchanged by transport mode

---

### Requirement: Body message coordination via OnBodyMessage
Body data (from outbound body pumps) arrives as typed messages via `OnBodyMessage(object msg)`. When
`_transport` is null, the state machine MUST encode and emit the chunk via `ops.OnOutbound(TransportData)`,
unchanged from prior behavior. When `_transport != null`, the state machine MUST encode the chunk directly
into `_transport` via `GetMemory`/`Advance` and MUST call `RequestFlush()`.

#### Scenario: Body pump delivers chunks to the state machine (legacy)
- **WHEN** the body pump produces a data chunk and `_transport == null`
- **THEN** it is delivered to the state machine via `OnBodyMessage`
- **THEN** the state machine MUST encode and emit the chunk via `ops.OnOutbound`, unchanged from prior
  behavior

#### Scenario: Body pump delivers chunks to the state machine (pipe mode)
- **WHEN** the body pump produces a data chunk and `_transport != null`
- **THEN** it is delivered to the state machine via `OnBodyMessage`
- **THEN** the state machine MUST encode and write the chunk directly into `_transport`
- **THEN** the state machine MUST call `RequestFlush()`

#### Scenario: Body pump signals completion
- **WHEN** the body pump signals that all body data has been sent
- **THEN** the state machine MUST emit the end-of-body marker on the wire (e.g., zero-length chunk for
  chunked encoding, END_STREAM flag for H2/H3) via whichever mode (`ops.OnOutbound` or `_transport`) is
  active
- **THEN** in pipe mode, the state machine MUST call `RequestFlush()` after emitting the marker

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

### Requirement: Construction requires stage operations callback
Every state machine (client and server) MUST accept its protocol-specific options and an
`IClientStageOperations` or `IServerStageOperations` instance at construction. The operations interface MUST
expose `IActorRef Self` for `PipeTo` bridging, in addition to its existing members. The operations callback
is the sole channel through which the state machine communicates outward. A state machine MUST NOT capture
or use any Akka infrastructure directly beyond `ops.Self` for `PipeTo`.

#### Scenario: Client state machine construction
- **WHEN** an `IClientStateMachine` is constructed
- **THEN** it MUST accept an `IClientStageOperations` instance
- **THEN** it MUST NOT call any operations methods during construction (defer to `PreStart`)

#### Scenario: Server state machine construction
- **WHEN** an `IServerStateMachine` is constructed
- **THEN** it MUST accept an `IServerStageOperations` instance
- **THEN** it MUST NOT call any operations methods during construction (defer to `PreStart`)

#### Scenario: Operations interface exposes Self
- **WHEN** the SM (in pipe mode) needs to bridge an async result to the actor thread
- **THEN** `_ops.Self` MUST return the StageActor's `IActorRef`
- **AND** the SM calls `PipeTo(_ops.Self, ...)` directly
