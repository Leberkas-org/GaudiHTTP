## MODIFIED Requirements

### Requirement: Client engine composition
Each HTTP version has a dedicated `IClientProtocolEngine` implementation that creates a
`BidiFlow<HttpRequestMessage, ITransportOutbound, ITransportInbound, HttpResponseMessage>`. The engine
wraps a version-specific `GraphStage<ClientConnectionShape>` containing an
`HttpClientConnectionStageLogic<TSM>` parameterized by the protocol's state machine type.

For TCP protocols (H1.0, H1.1, H2), the network port between protocol stage and transport stage
carries ONLY lifecycle events (`TransportConnected(IConnectionTransport)`,
`TransportDisconnected` inbound). Byte data flows through `IConnectionTransport` (Pipe) directly,
bypassing the Akka Streams port entirely. The stage logic MUST NOT create, push, grab, or queue
`TransportData` items on either port direction for TCP protocols. The `_outboundQueue` MUST NOT
contain `TransportData` items.

For QUIC (H3), the network port continues to carry both lifecycle events and multiplexed data;
TCP-only scenarios do not apply to H3.

#### Scenario: Engine creates a BidiFlow with the correct shape
- **WHEN** an `IClientProtocolEngine.CreateFlow()` is called (Http10, Http11, Http20, Http30)
- **THEN** it returns a `BidiFlow` whose top half accepts `HttpRequestMessage` and emits `ITransportOutbound`
- **AND** whose bottom half accepts `ITransportInbound` and emits `HttpResponseMessage`

#### Scenario: TCP client stage logic never handles TransportData on the port
- **WHEN** the protocol is TCP-based (H1.0, H1.1, H2)
- **THEN** `HttpClientConnectionStageLogic` MUST NOT `Grab(_inNetwork)` a `TransportData` item
- **AND** MUST NOT `Push(_outNetwork, ...)` a `TransportData` item
- **AND** MUST NOT enqueue `TransportData` in `_outboundQueue`
- **AND** all byte-level I/O happens through the SM's `IConnectionTransport` reference, not the port

#### Scenario: TCP server stage logic never handles TransportData on the port
- **WHEN** the protocol is TCP-based (H1.0, H1.1, H2)
- **THEN** `HttpServerConnectionStageLogic` MUST NOT `Grab` or `Push` `TransportData` items
- **AND** MUST NOT enqueue `TransportData` in any outbound queue
- **AND** all byte-level I/O happens through the SM's `IConnectionTransport` reference

#### Scenario: Client stage routes lifecycle events and async results to the SM
- **WHEN** `TransportConnected` or `TransportDisconnected` arrives on `_inNetwork`
- **THEN** the stage logic calls `_sm.OnTransportEvent(item)` and re-pulls `_inNetwork`
- **AND WHEN** the StageActor receives any other message (read/flush completion, pump status, body
  messages)
- **THEN** the stage logic calls `_sm.OnAsyncResult(msg)` without inspecting the message's contents
