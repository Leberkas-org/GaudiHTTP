# Streams/Stages Pipeline

The Streams layer composes Akka Streams `BidiFlow` and `GraphStage` components into a
complete HTTP pipeline. On the client side, feature stages (cache, redirect, retry, cookies,
content encoding, tracing, user handlers) wrap a protocol-specific connection stage that bridges
to a transport flow. On the server side, a connection stage bridges transport data to
`IFeatureCollection`-based request/response exchange with an `ApplicationBridgeStage` that
invokes the ASP.NET Core `IHttpApplication<TContext>`.

## Requirements

### Requirement: Client engine composition

Each HTTP version has a dedicated `IClientProtocolEngine` implementation that creates a
`BidiFlow<HttpRequestMessage, ITransportOutbound, ITransportInbound, HttpResponseMessage>`. The engine
wraps a version-specific `GraphStage<ClientConnectionShape>` containing an
`HttpClientConnectionStageLogic<TSM>` parameterized by the protocol's state machine type. The `Engine`
class joins the protocol BidiFlow with a transport flow from `TransportRegistry` and wraps the result in
the feature pipeline via `FeaturePipelineBuilder`.

For TCP protocols (H1.0, H1.1, H2), the network port between protocol stage and transport stage carries
ONLY lifecycle events (`TransportConnected(IConnectionTransport)`, `TransportDisconnected` inbound). Byte
data flows through `IConnectionTransport` (Pipe) directly, bypassing the Akka Streams port entirely — the
stage logic does not grab or push `TransportData` on the TCP path. For QUIC (H3), the network port
continues to carry both lifecycle events and multiplexed data; this requirement's TCP-only scenarios do
not apply to H3.

#### Scenario: Engine creates a BidiFlow with the correct shape
- **WHEN** an `IClientProtocolEngine.CreateFlow()` is called (Http10, Http11, Http20, Http30)
- **THEN** it returns a `BidiFlow` whose top half accepts `HttpRequestMessage` and emits `ITransportOutbound`
- **AND** whose bottom half accepts `ITransportInbound` and emits `HttpResponseMessage`

#### Scenario: Engine flow is joined with a transport
- **WHEN** `ProtocolCoreBuilder.Build` creates the flow for an endpoint
- **THEN** the engine BidiFlow is joined with the transport flow from `TransportRegistry.Get(version)`
- **AND** the joined flow runs in its own async boundary (`.Async()`)

#### Scenario: TCP client stage logic never handles TransportData on the port
- **WHEN** the protocol is TCP-based (H1.0, H1.1, H2)
- **THEN** `HttpClientConnectionStageLogic` MUST NOT `Grab(_inNetwork)` a `TransportData` item
- **AND** MUST NOT `Push(_outNetwork, ...)` a `TransportData` item
- **AND** all byte-level I/O happens through the SM's `IConnectionTransport` reference, not the port

#### Scenario: TCP stage-test uses pipe transport for data
- **WHEN** a TCP protocol stage-test uses `TestConnectionStage` with `AutoConnectWithTransport()`
- **THEN** `TransportConnected` MUST carry a non-null `IConnectionTransport`
- **AND** the SM MUST read inbound data via `transport.ReadAsync()`
- **AND** the SM MUST write outbound data via `transport.GetMemory`/`Advance`/`FlushAsync`
- **AND** no `TransportData` items MUST flow through the Akka Streams port

#### Scenario: Test feeds response data via pipe transport
- **WHEN** a stage-test needs to provide response data to the SM
- **THEN** it MUST call `stage.Transport!.FeedInput(data)` instead of `stage.PushData(data)`
- **AND** the SM receives it via its pipe read loop

#### Scenario: Test reads request data from pipe transport
- **WHEN** a stage-test needs to capture encoded request data from the SM
- **THEN** it MUST call `stage.Transport!.ReadOutputAsync()` or `TryReadOutput()`
- **AND** no `TransportData` items are available via `stage.ReceivedOutbound`

#### Scenario: Client stage routes lifecycle events and async results to the SM
- **WHEN** `TransportConnected` or `TransportDisconnected` arrives on `_inNetwork`
- **THEN** the stage logic calls `_sm.OnTransportEvent(item)` and re-pulls `_inNetwork`
- **AND WHEN** the StageActor receives any other message (read/flush completion, pump status, body pump
  messages)
- **THEN** the stage logic calls `_sm.OnAsyncResult(msg)` without inspecting the message's contents

#### Scenario: Version selection is per-endpoint
- **WHEN** a request targets a `RequestEndpoint` with a specific HTTP version
- **THEN** `ProtocolCoreBuilder` selects the matching `IClientProtocolEngine` (1.0, 1.1, 2.0, 3.0)
- **AND** an unrecognized version throws `ArgumentOutOfRangeException`

---

### Requirement: Server engine composition

Each HTTP version has a dedicated `IServerProtocolEngine` implementation that creates a
`BidiFlow<ITransportInbound, IFeatureCollection, IFeatureCollection, ITransportOutbound>`. The engine
wraps a version-specific `GraphStage<ServerConnectionShape>` containing an
`HttpServerConnectionStageLogic<TSM>` parameterized by the server state machine type. A
`NegotiatingServerEngine` delegates to a `ProtocolNegotiatorConnectionStage` that selects the protocol at
runtime based on ALPN negotiation.

For TCP protocols, the same thin-stage-logic pattern as the client side applies: the network port carries
only lifecycle events, and connection completion after the last response is driven by
`IConnectionTransport.CompleteOutput()` rather than draining an outbound queue through the port.

#### Scenario: Server engine creates a BidiFlow with the correct shape
- **WHEN** an `IServerProtocolEngine.CreateFlow()` is called
- **THEN** it returns a `BidiFlow` whose top half accepts `ITransportInbound` and emits `IFeatureCollection`
- **AND** whose bottom half accepts `IFeatureCollection` and emits `ITransportOutbound`

#### Scenario: Protocol negotiation selects the correct state machine
- **WHEN** `NegotiatingServerEngine` is used with `HttpProtocols.Http1AndHttp2`
- **THEN** the `ProtocolNegotiatorConnectionStage` delegates to a `ProtocolNegotiatingStateMachine`
- **AND** that state machine selects the wire protocol based on ALPN negotiation results from `TransportConnected`

#### Scenario: ProtocolRouter resolves the correct engine
- **WHEN** `ProtocolRouter.ResolveEngine` is called with a specific version
- **THEN** it returns the matching `IServerProtocolEngine` (1.0, 1.1, 2.0, 3.0)
- **AND** an unrecognized version falls back to HTTP/1.1

#### Scenario: TCP server stage logic never handles TransportData on the port
- **WHEN** the server protocol is TCP-based (H1.0, H1.1, H2)
- **THEN** `HttpServerConnectionStageLogic` MUST NOT `Grab(_inNetwork)` or `Push(_outNetwork, ...)` a
  `TransportData` item
- **AND** the stage logic routes lifecycle events to `_sm.OnTransportEvent(item)` and StageActor messages
  to `_sm.OnAsyncResult(msg)`, matching the client-side pattern

#### Scenario: TransportConnected populates connection features from IConnectionTransport
- **WHEN** `TransportConnected(IConnectionTransport)` arrives at the server stage
- **THEN** the stage extracts remote/local IP endpoints from `IConnectionTransport.Info` and creates a
  `GaudiHttpConnectionFeature`
- **AND** if TLS info is present, creates a `TlsHandshakeFeature`
- **AND** forwards the event to `_sm.OnTransportEvent(item)`

#### Scenario: Connection completion drains via CompleteOutput, not a port-level queue
- **WHEN** the server state machine signals completion after the last response (e.g., `Connection: close`,
  GOAWAY, or client-initiated shutdown)
- **THEN** the stage calls `_transport.CompleteOutput()` instead of draining an `_outboundQueue` through
  `_outNetwork`
- **AND** the stage completes only after observing the write pump's completion (routed through
  `_sm.OnAsyncResult` as a pump-completion message), not by polling port availability

---

### Requirement: Feature pipeline composition (BidiStage stacking)

The `FeaturePipelineBuilder` composes zero or more feature BidiStages on top of the engine
flow. Each feature stage has a `BidiShape<HttpRequestMessage, HttpRequestMessage,
HttpResponseMessage, HttpResponseMessage>` -- transforming requests on the way in and
responses on the way out. Stages are collected innermost-first into a list and wired
in a single `GraphDsl.Create` call to avoid intermediate `BidiFlow` wrappers.

#### Scenario: Stacking order is deterministic (innermost to outermost)
- **WHEN** the `FeaturePipelineBuilder.Build` composes the pipeline
- **THEN** the stacking order from innermost (closest to engine) to outermost is:
  1. AltSvcBidiStage
  2. ContentEncodingBidiStage
  3. CacheBidiStage
  4. ExpectContinueBidiStage
  5. RetryBidiStage
  6. CookieBidiStage
  7. RedirectBidiStage
  8. HandlerBidiStage(s) (user handlers, FIFO: `Handlers[0]` is outermost)
  9. TracingBidiStage
- **AND** request flow goes outer-to-inner: Tracing -> Handlers -> Redirect -> Cookie -> Retry -> Expect100 -> Cache -> ContentEncoding -> AltSvc -> Engine
- **AND** response flow goes inner-to-outer: Engine -> AltSvc -> ContentEncoding -> Cache -> Expect100 -> Retry -> Cookie -> Redirect -> Handlers -> Tracing

#### Scenario: Stages are conditionally included
- **WHEN** a feature is not configured in the `PipelineDescriptor` (e.g., `CookieJar` is null)
- **THEN** the corresponding BidiStage is not added to the layers list
- **AND** the pipeline operates without it (zero overhead)

#### Scenario: Empty pipeline is a pass-through
- **WHEN** no features are configured (all policy/store/jar fields are null, no handlers, tracing inactive)
- **THEN** `FeaturePipelineBuilder.Build` returns the engine flow unchanged (no `GraphDsl` wrapper)

#### Scenario: Single GraphDsl wiring avoids intermediate objects
- **WHEN** multiple feature stages are active
- **THEN** all BidiShapes and the engine flow are wired in a single `GraphDsl.Create` call
- **AND** no iterative `BidiFlow.Atop` or separate `BidiFlow.Join(engineFlow)` calls are used
- **AND** this eliminates the intermediate `BidiFlow` object that `Atop`/`Join` stacking would produce

---

### Requirement: PipelineDescriptor configuration

The `PipelineDescriptor` record carries all feature configuration needed by
`FeaturePipelineBuilder`. It is an immutable snapshot of the client's desired pipeline behavior.

#### Scenario: PipelineDescriptor carries all feature toggles
- **WHEN** a `PipelineDescriptor` is constructed
- **THEN** it contains: `RedirectPolicy`, `RetryPolicy`, `Expect100Policy`, `CompressionPolicy`, `CookieJar`, `CacheStore`, `CachePolicy`, `Handlers`, `AutomaticDecompression`, `AltSvcCache`, `UseProxy`, `Proxy`

#### Scenario: PipelineDescriptor.Empty disables all features
- **WHEN** `PipelineDescriptor.Empty` is used
- **THEN** all policy/store/jar fields are null, `Handlers` is empty, and `AutomaticDecompression` defaults to true

---

### Requirement: Client connection stage lifecycle

`HttpClientConnectionStageLogic<TSM>` is a `TimerGraphStageLogic` with a 4-port
`ClientConnectionShape` (InNetwork, OutResponse, InRequest, OutNetwork). It bridges Akka
Streams port handlers to the protocol state machine via `IClientStageOperations`.

#### Scenario: PreStart initializes the stage actor and state machine
- **WHEN** the stage materializes
- **THEN** `PreStart` creates a stage actor via `GetStageActor` (for body pump messages)
- **AND** registers a cancel callback via `GetAsyncCallback<HttpRequestMessage>`
- **AND** calls `_sm.PreStart()` on the state machine

#### Scenario: Inbound network data is decoded by the state machine
- **WHEN** data arrives on `InNetwork` (transport push)
- **THEN** the stage grabs the `ITransportInbound` and calls `_sm.DecodeServerData(item)`
- **AND** if decoding throws, the stage fails with `FailStage(ex)` to prevent desynchronization

#### Scenario: Requests are fed to the state machine with cancellation tracking
- **WHEN** a request arrives on `InRequest`
- **THEN** the stage calls `_sm.OnRequest(request)`
- **AND** if the request's `CancellationToken` is cancelable, registers an `UnsafeRegister` callback
- **AND** pull on `InRequest` is gated by `_sm.CanAcceptRequest`

#### Scenario: Responses are pushed or queued
- **WHEN** the state machine calls `IClientStageOperations.OnResponse(response)`
- **THEN** if `OutResponse` is available, the response is pushed immediately
- **AND** otherwise it is enqueued in `_responseQueue`

#### Scenario: OnOutbound pushes without type-checking (updated)
- **WHEN** the SM calls `_ops.OnOutbound(item)` with any `ITransportOutbound`
- **THEN** the stage logic MUST push (or queue) the item to the outbound network port
- **AND** the stage logic MUST NOT check whether the item is `TransportData`
- **AND** no `BridgeTransportData` method MUST exist

#### Scenario: Network pull is gated by ShouldPauseNetwork
- **WHEN** the stage attempts to pull `InNetwork`
- **THEN** the pull is suppressed if `_sm.ShouldPauseNetwork` is true
- **AND** this enables body backpressure (the state machine pauses network reads when body buffers are full)

#### Scenario: Stage actor receives body pump messages
- **WHEN** a body pump sends a message to the stage actor
- **THEN** the stage calls `_sm.OnBodyMessage(msg)` on the actor thread
- **AND** re-evaluates network pull and request pull afterwards

#### Scenario: Drain-complete uses a settle timer
- **WHEN** `InRequest` is closed and no in-flight requests, reconnects, or queued items remain
- **THEN** the stage schedules a 100ms `drain-complete` timer instead of completing synchronously
- **AND** this prevents dropping a body pump completion message that is already in the stage actor mailbox

#### Scenario: PostStop does not dispose TransportData from queue (updated)
- **WHEN** the stage logic tears down in `PostStop`
- **THEN** all `CancellationTokenRegistration`s are disposed
- **AND** the `_outboundQueue` drain loop MUST NOT check for `TransportData` items
- **AND** no `WireBuffer` disposal MUST occur in the stage logic
- **AND** queued responses are disposed
- **AND** `_sm.Cleanup()` is called

#### Scenario: Stage logic does not track flush state (updated)
- **WHEN** outbound data is flushed to the pipe
- **THEN** the stage logic MUST NOT maintain `_flushInProgress`, `_flushGen`, or `_transport` fields
- **AND** no `BridgeFlushCompleted` / `BridgeFlushFailed` messages MUST exist

---

### Requirement: Server connection stage lifecycle

`HttpServerConnectionStageLogic<TSM>` is a `TimerGraphStageLogic` with a 4-port
`ServerConnectionShape` (InNetwork, OutRequest, InResponse, OutNetwork). It bridges Akka
Streams port handlers to the server state machine via `IServerStageOperations`.

#### Scenario: PreStart pulls network immediately
- **WHEN** the server stage materializes
- **THEN** `PreStart` creates a stage actor, calls `_sm.PreStart()`, and pulls `InNetwork`
- **AND** this is different from the client stage, which waits for downstream demand

#### Scenario: TransportConnected populates connection features
- **WHEN** the first `ITransportInbound` is a `TransportConnected`
- **THEN** the stage extracts remote/local IP endpoints and creates a `GaudiHttpConnectionFeature`
- **AND** if TLS info is present, creates a `TlsHandshakeFeature`
- **AND** these features are made available to the state machine via `IServerStageOperations`

#### Scenario: Handler dispatch is gated by MaxConcurrentRequests
- **WHEN** the state machine calls `IServerStageOperations.OnRequest(features)`
- **THEN** the request is enqueued in `_requestQueue`
- **AND** dispatch to `OutRequest` is gated by `_handlerInFlight < _sm.MaxConcurrentRequests`
- **AND** HTTP/1.x returns 1 (serializes pipelined dispatch for RFC 9112 response ordering)
- **AND** multiplexed protocols return `int.MaxValue` (unbounded)

#### Scenario: Response processing returns features and checks completion
- **WHEN** a response arrives on `InResponse` from the application bridge
- **THEN** `_handlerInFlight` is decremented and `_sm.OnResponse(features)` is called
- **AND** if `_sm.ShouldComplete` is true, the stage flushes the outbound queue then completes
- **AND** if the response has no body feature, the `IFeatureCollection` is returned to the pool

#### Scenario: GOAWAY flushes before closing
- **WHEN** the state machine signals `ShouldComplete` (e.g., after sending GOAWAY)
- **THEN** the stage sets `_completeAfterFlush = true` and drains `_outboundQueue`
- **AND** `CompleteStage()` is called only after the last outbound item is pushed

#### Scenario: Body resumed re-pulls network
- **WHEN** the stage actor receives a `BodyResumed` message
- **THEN** the stage calls `_sm.ResumeBody()` and attempts to re-pull `InNetwork`
- **AND** this resumes network reads that were paused due to body backpressure

---

### Requirement: Stage logic is pure plumbing for TCP connections

`HttpClientConnectionStageLogic` and `HttpServerConnectionStageLogic` MUST NOT contain transport
data bridging, flush tracking, or outbound data interception. Their responsibilities are:

1. **Port handlers** -- grab lifecycle items from network port, route to SM.
2. **StageActor message routing** -- route all messages to `_sm.OnBodyMessage(msg)`.
3. **Timer delegation** -- route timer fires to `_sm.OnTimerFired(name)`.
4. **OnOutbound** -- push/queue `ITransportOutbound` items to the network port (lifecycle commands
   only for TCP; no `TransportData` interception).

---

### Requirement: IClientStageOperations bridge contract

`IClientStageOperations` is the interface through which protocol state machines interact
with the Akka Streams stage logic. It provides push/queue operations for responses and
outbound data, timer management, and access to the stage actor.

#### Scenario: OnResponse pushes or queues responses
- **WHEN** a state machine produces a response
- **THEN** it calls `OnResponse(response)` which pushes to `OutResponse` if available, else enqueues

#### Scenario: OnOutbound pushes or queues transport items
- **WHEN** a state machine produces outbound data (encoded frames)
- **THEN** it calls `OnOutbound(item)` which pushes to `OutNetwork` if available, else enqueues

#### Scenario: Timer operations delegate to Akka
- **WHEN** a state machine needs a timer (keep-alive, request timeout, data rate)
- **THEN** it calls `OnScheduleTimer(name, duration)` / `OnCancelTimer(name)` which map to `ScheduleOnce` / `CancelTimer`

#### Scenario: StageActor is available for body pump messaging
- **WHEN** a state machine or body pump needs to send actor messages (body completions, flush events)
- **THEN** it uses `StageActor` to `Tell` messages that will be processed on the stage thread

#### Scenario: HasPendingDemand reflects outbound availability
- **WHEN** the state machine checks whether the transport has demand
- **THEN** `HasPendingDemand` returns true only when `_outboundQueue` is empty AND `OutNetwork` is available

---

### Requirement: IServerStageOperations bridge contract

`IServerStageOperations` extends the bridge pattern for the server side with additional
server-specific operations.

#### Scenario: OnRequest queues parsed requests
- **WHEN** a server state machine has parsed a complete request
- **THEN** it calls `OnRequest(features)` which enqueues the `IFeatureCollection` for dispatch
- **AND** if the queue exceeds `MaxQueuedRequests`, the connection is closed

#### Scenario: OnResponseBodyComplete returns feature collections
- **WHEN** a response body has been fully written to the transport
- **THEN** `OnResponseBodyComplete(features)` returns the `IFeatureCollection` to the pool via `FeatureCollectionFactory.Return`

#### Scenario: Server-specific context is available
- **WHEN** a state machine needs server-side context
- **THEN** `IServerStageOperations` provides `Log`, `Materializer`, `Services`, `ConnectionFeature`, and `TlsHandshakeFeature`

---

### Requirement: IFeatureStageOperations for feature stages

Feature stages (Cache, Redirect, Retry, ContentEncoding, Tracing) use
`IFeatureStageOperations` as their bridge to the stage logic, providing typed push/pull
operations for the BidiShape ports.

#### Scenario: Feature stages push and pull through the bridge
- **WHEN** a feature stage logic implements `IFeatureStageOperations`
- **THEN** it can call `OnPushRequest`, `OnPushResponse`, `OnSignalPullRequest`, `OnSignalPullResponse`
- **AND** `OnCompleteStage` to terminate the pipeline
- **AND** `OnScheduleTimer` / `OnCancelTimer` for delayed operations (retry backoff, cache TTL)

---

### Requirement: Client endpoint routing

`ProtocolCoreBuilder` groups requests by `RequestEndpoint` (scheme, host, port, version) and
dispatches each substream through a lazily materialized version-specific connection flow.

#### Scenario: Requests are grouped by endpoint
- **WHEN** multiple requests flow through the client pipeline
- **THEN** `GroupByRequestEndpointStage` groups them by `RequestEndpoint.FromRequest`
- **AND** each substream contains requests for a single endpoint

#### Scenario: Connection flow is lazily materialized per substream
- **WHEN** a new substream starts (first request for an endpoint)
- **THEN** `EndpointDispatchStage` calls the flow factory to create the engine + transport flow
- **AND** subsequent requests in the same substream reuse the materialized flow

#### Scenario: Flow blueprints are cached per endpoint
- **WHEN** `EndpointDispatchStage` materializes a flow for an endpoint
- **THEN** the flow blueprint is cached in a `ConcurrentDictionary<RequestEndpoint, Flow>`
- **AND** new substreams for the same endpoint reuse the cached blueprint

#### Scenario: Concurrency limits are version-specific
- **WHEN** `ProtocolCoreBuilder` configures substream limits
- **THEN** `MaxSubstreamsPerKey` uses `MaxConnectionsPerServer` from the version-specific options
- **AND** `MaxConcurrencyPerSlot` uses pipeline depth (H1.1), max concurrent streams (H2, H3), or 1 (H1.0)
- **AND** H1.0 is always 1 connection, 1 concurrent request

---

### Requirement: Feature stage contracts

Each feature BidiStage follows a common pattern: it transforms requests and/or responses
flowing through the pipeline, is conditionally included based on configuration, and becomes
a zero-overhead pass-through when its policy/store is null.

#### Scenario: AltSvcBidiStage upgrades request version (innermost)
- **WHEN** a request targets a host with a cached Alt-Svc HTTP/3 entry
- **THEN** the request version is upgraded to 3.0 before protocol routing
- **AND** the advertised port/host from the Alt-Svc entry is applied
- **AND** on the response path, Alt-Svc headers are parsed and cached for future requests
- **AND** when a forward proxy applies, the HTTP/3 upgrade is skipped (QUIC cannot traverse HTTP proxies)

#### Scenario: ContentEncodingBidiStage handles compression and decompression
- **WHEN** a request body meets the compression threshold (request direction)
- **THEN** the body is compressed per the `CompressionPolicy`
- **AND** on the response path, Content-Encoding (gzip, deflate, br) is decompressed when `AutomaticDecompression` is true
- **AND** the Content-Encoding header is removed and Content-Length updated after decompression

#### Scenario: CacheBidiStage short-circuits on cache hit
- **WHEN** a cacheable request matches a fresh entry in the `Cache` store
- **THEN** the cached response is pushed directly on the response output (Out2), bypassing the engine
- **AND** on a cache miss, the request is forwarded to the engine unchanged
- **AND** on a must-revalidate, a conditional request (If-None-Match / If-Modified-Since) is built
- **AND** a 304 Not Modified response merges headers with the cached entry and pushes 200 OK
- **AND** unsafe methods (POST, PUT, DELETE) invalidate the cache entry

#### Scenario: ExpectContinueBidiStage adds Expect: 100-continue
- **WHEN** a request body size meets or exceeds the threshold from `Expect100Policy`
- **THEN** the `Expect: 100-continue` header is added to the request
- **AND** a 100 Continue response is consumed silently (body release signal)
- **AND** a 417 Expectation Failed is forwarded to the caller

#### Scenario: RetryBidiStage re-injects failed requests
- **WHEN** a response is evaluated as retryable by `RetryPolicy` (via `RetryEvaluator`)
- **THEN** the response is disposed and the original request is re-injected on the request output
- **AND** retry requests take priority over new requests from the upstream
- **AND** `Retry-After` delays use stage timers
- **AND** at most `MaxPendingRetries` (16) can be queued before backpressure is applied

#### Scenario: CookieBidiStage manages cookies per RFC 6265
- **WHEN** a request is pushed through the stage
- **THEN** cookies from the `CookieJar` are injected into the request headers (RFC 6265 SS5.4)
- **AND** on the response path, `Set-Cookie` headers are stored in the `CookieJar` (RFC 6265 SS5.3)

#### Scenario: RedirectBidiStage follows redirects internally
- **WHEN** a response is a redirect (3xx) and the `RedirectPolicy` allows following
- **THEN** the redirect response is consumed and a new request is emitted on the request output
- **AND** each request chain gets its own `RedirectHandler` tracked via `HttpRequestMessage.Options`
- **AND** redirect requests take priority over new requests from upstream
- **AND** visited URIs and redirect count are tracked per chain to detect loops

#### Scenario: HandlerBidiStage wraps user GaudiHandlers
- **WHEN** user-registered `GaudiHandler` instances are present
- **THEN** each is wrapped in a `HandlerBidiStage` with a unique index
- **AND** `ProcessRequest` is called on the request path, `ProcessResponse` on the response path
- **AND** handlers are ordered FIFO: `Handlers[0]` is outermost (first to see requests, last to see responses)

#### Scenario: TracingBidiStage manages Activity lifecycle (outermost)
- **WHEN** an `ActivityListener` is subscribed to the GaudiHTTP `ActivitySource`
- **THEN** TracingBidiStage is added as the outermost layer
- **AND** it starts a root "GaudiHTTP.ClientRequest" Activity on the request path
- **AND** stops the Activity with `http.response.status_code` on the response path
- **AND** when no listener is subscribed, it is zero-overhead (not added to the pipeline)

---

### Requirement: Server application bridge

`ApplicationBridgeStage<TContext>` is a `FlowShape<IFeatureCollection, IFeatureCollection>`
stage that invokes the ASP.NET Core `IHttpApplication<TContext>` for each request. It sits
between the server connection stage's `OutRequest` and `InResponse` ports.

#### Scenario: Requests are dispatched with bounded parallelism
- **WHEN** requests arrive at the bridge
- **THEN** they are dispatched to `IHttpApplication<TContext>` with up to `_parallelism` concurrent handlers

#### Scenario: Handler timeout and grace period enforce deadlines
- **WHEN** a handler exceeds `_handlerTimeout`
- **THEN** a soft cancellation is signaled via `CancellationTokenSource`
- **AND** if the handler does not complete within `_handlerGracePeriod`, a hard abort is applied

#### Scenario: Completed responses are pushed in completion order
- **WHEN** a handler completes (success or failure)
- **THEN** the response `IFeatureCollection` is pushed to the output
- **AND** sequence tracking ensures correct ordering

---

### Requirement: Port naming convention

All stage ports follow the `StageName.Direction.Role` naming convention (PascalCase).

#### Scenario: Connection stage ports use the connection shape
- **WHEN** a client connection stage is created
- **THEN** its ports are `InNetwork`, `OutResponse`, `InRequest`, `OutNetwork`

#### Scenario: Feature stage ports follow StageName.Direction.Role
- **WHEN** a feature BidiStage is created (e.g., CacheBidiStage)
- **THEN** its ports are named `Cache.In.Request`, `Cache.Out.Request`, `Cache.In.Response`, `Cache.Out.Response`
- **AND** the `Stage` suffix is dropped from the name

#### Scenario: Handler stage ports include the index
- **WHEN** a `HandlerBidiStage` is created with handler type `MyHandler` at index 2
- **THEN** its ports are named `MyHandler2.In.Request`, `MyHandler2.Out.Request`, etc.

---

### Requirement: TransportRegistry maps versions to transport flows

`TransportRegistry` maintains a dictionary of transport flows keyed by HTTP version. Each
version's `Flow<ITransportOutbound, ITransportInbound, NotUsed>` represents the wire-level
transport (TCP, TLS, QUIC).

#### Scenario: Transport lookup succeeds for registered versions
- **WHEN** `TransportRegistry.Get(version)` is called for a registered version
- **THEN** it returns the corresponding transport flow

#### Scenario: Transport lookup fails for unregistered versions
- **WHEN** `TransportRegistry.Get(version)` is called for an unregistered version
- **THEN** it throws `InvalidOperationException` listing the registered versions

---

### Requirement: Upstream failure resilience in feature stages

Feature stages absorb upstream failures on the request path to prevent cascading stage
failures in the pipeline. The pattern is consistent across all feature stages.

#### Scenario: Request upstream failure is absorbed
- **WHEN** the request upstream fails with an exception
- **THEN** the feature stage logs a warning and completes its request output (does not propagate the failure)
- **AND** this prevents a single request failure from tearing down the entire connection pipeline
