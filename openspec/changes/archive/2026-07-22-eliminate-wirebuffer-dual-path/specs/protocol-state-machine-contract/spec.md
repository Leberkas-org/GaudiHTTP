## MODIFIED Requirements

### Requirement: Inbound data arrives via DecodeServerData / DecodeClientData
For TCP protocols (H1.0, H1.1, H2), `DecodeServerData`/`DecodeClientData` MUST only dispatch
lifecycle events (`TransportConnected`, `TransportDisconnected`) via `DispatchLifecycleEvent`.
Byte data MUST NOT arrive through these methods for TCP protocols — all byte data is delivered
by `TransportIo.ProcessReadResult` → `DecodeData(ReadOnlySequence<byte>)` via the pipe read loop.

If a `TransportData` item is received on a TCP SM's `DecodeServerData`/`DecodeClientData` when
`Transport` is null, the SM MUST throw `InvalidOperationException` — this indicates a programming
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

## REMOVED Requirements

### Requirement: TransportData fallback for pre-connect
**Reason**: The dual-path `if (Transport is null && data is TransportData)` fallback is dead code
in production. Transport is always connected before data flows. Tests migrate to `InMemoryTransport`.
**Migration**: Replace `sm.DecodeClientData(TransportData.Rent(wireBuffer))` with
`transport.Feed(data)` + `sm.TryHandleAsyncResult(readCompleted)` in all test code.
