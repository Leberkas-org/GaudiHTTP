## MODIFIED Requirements

### Requirement: Inbound data arrives via DecodeServerData / DecodeClientData
For TCP protocols (H1.0, H1.1, H2), `DecodeServerData`/`DecodeClientData` MUST only dispatch
lifecycle events (`TransportConnected`, `TransportDisconnected`) via `DispatchLifecycleEvent`.
If a `TransportData` item is received on a TCP SM, the SM MUST throw
`InvalidOperationException`. There is no fallback path for `TransportData` on TCP SMs.

For QUIC/H3 protocols, `DecodeClientData`/`DecodeServerData` is unchanged.

#### Scenario: TransportConnected routed through base class
- **WHEN** `TransportConnected` is received in `DecodeServerData` / `DecodeClientData`
- **THEN** the SM MUST call the base class lifecycle dispatch
- **THEN** the base class MUST initialize the transport, start the read loop, and call `OnTransportConnected`

#### Scenario: TransportData on TCP SM throws
- **WHEN** `TransportData` is received on a TCP SM's `DecodeServerData`/`DecodeClientData`
- **THEN** the SM MUST throw `InvalidOperationException`

#### Scenario: Byte data decoded via pipe read loop
- **WHEN** the SM's read loop delivers a `ReadResult` with `_transport != null`
- **THEN** the SM MUST call `DecodeData(ReadOnlySequence<byte>)` to process the bytes
- **THEN** the SM MUST call `_transport.AdvanceTo(consumed, examined)` after decode
