## MODIFIED Requirements

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
