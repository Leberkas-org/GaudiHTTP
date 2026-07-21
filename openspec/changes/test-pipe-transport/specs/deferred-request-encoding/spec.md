## ADDED Requirements

### Requirement: H1.1 Client SM defers request encoding until transport is connected
The H1.1 Client SM MUST NOT encode request headers or emit `TransportData` in `OnRequest` when
`Transport` is null. Instead, it MUST buffer the pending request and encode it in
`OnTransportConnected` after the transport becomes available.

#### Scenario: First request triggers connect then deferred encode
- **WHEN** `OnRequest` is called with `Transport` null (first request, no connection yet)
- **THEN** the SM MUST enqueue the request in `_inFlightQueue`
- **AND** emit `ConnectTransport` via `Ops.OnOutbound`
- **AND** set a `_pendingEncode` flag
- **AND** MUST NOT call `_encoder.WriteTo` or `Transport.GetMemory`
- **AND** MUST NOT call `Ops.OnOutbound(TransportData.Rent(...))`

#### Scenario: TransportConnected triggers deferred encode
- **WHEN** `OnTransportConnected` fires and `_pendingEncode` is true
- **THEN** the SM MUST encode the pending request using `Transport.GetMemory`/`Advance`
- **AND** call `RequestFlush()`
- **AND** clear `_pendingEncode`

#### Scenario: Subsequent request with active transport encodes immediately
- **WHEN** `OnRequest` is called with `Transport` not null (connection already established)
- **THEN** the SM MUST encode the request immediately using `Transport.GetMemory`/`Advance`
- **AND** no deferred encoding is needed

### Requirement: H1.0 Client SM defers request encoding until transport is connected
The H1.0 Client SM MUST apply the same deferred encoding pattern as H1.1.

#### Scenario: First request with null transport defers encoding
- **WHEN** `OnRequest` is called with `Transport` null
- **THEN** the SM MUST enqueue the request, emit `ConnectTransport`, and defer encoding

#### Scenario: TransportConnected encodes the deferred request
- **WHEN** `OnTransportConnected` fires with a pending request
- **THEN** the SM MUST encode it via the pipe transport
