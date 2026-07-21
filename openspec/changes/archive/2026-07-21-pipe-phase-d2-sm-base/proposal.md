## Why

Phase D introduced pipe-based TCP transport but only converted the **inbound** (read) path to use `IConnectionTransport` directly from the SM layer. The **outbound** (write) path still allocates `WireBuffer.Rent` + `TransportData.Rent` in every SM, routes through `_ops.OnOutbound`, and relies on a `BridgeTransportData` workaround in the stage logic that copies the data into the pipe — an extra allocation and copy per outbound packet. Only the H1.1 client SM was partially converted to direct pipe writes; the other five TCP SMs (H1.0 client/server, H1.1 server, H2 client/server) still use the old path exclusively. Additionally, every SM contains ~25 lines of identical `TransportIo` delegation boilerplate (lifecycle dispatch, `OnAsyncResult` routing, cleanup). This blocks Phase E cleanup because `TransportData`, `OnOutbound`, `_outboundQueue`, and the bridge are all live production code.

## What Changes

- **Extract `TransportIo` into a base class** (`TcpStateMachineBase<TOps>`) that owns transport lifecycle dispatch (`OnConnected`/`OnDisconnected`), the read loop, the write path (`Write`/`Flush` directly to `PipeWriter`), `OnAsyncResult` routing, and cleanup. Each SM becomes a subclass that provides a `Decode` callback and protocol-specific logic.
- **Convert all six TCP SM outbound paths** to write directly to `IConnectionTransport.GetMemory`/`Advance`/`FlushAsync` via the base class, eliminating `WireBuffer.Rent` + `TransportData.Rent` for outbound encode. Pre-connect buffering (client SMs only) moves into the base class.
- **Remove the `BridgeTransportData` workaround** from `HttpClientConnectionStageLogic` and `HttpServerConnectionStageLogic`, along with `BridgeFlushCompleted`/`BridgeFlushFailed` messages and the stage-level `_flushInProgress` tracking.
- **Remove `TransportData` usage on the TCP outbound path**. The `TransportData` class stays (QUIC uses it), but no TCP SM creates or pushes `TransportData` items through `OnOutbound`. The `OnOutbound` method on `IClientStageOperations`/`IServerStageOperations` carries lifecycle commands only (TCP).
- **Remove dead `TransportDataFlushed` handling** from H1.0/H1.1/H2 client and server SMs — no TCP source emits this event since Phase D removed it from the transport stage.

## Capabilities

### New Capabilities
- `sm-outbound-write`: Base class owned outbound write path — SMs call `Write(ReadOnlySpan<byte>)` or write to `IBufferWriter<byte>` (backed by `PipeWriter`) and `Flush()`, eliminating the `WireBuffer` → `TransportData` → bridge → copy → pipe roundtrip. Includes pre-connect buffering for client SMs.

### Modified Capabilities
- `protocol-state-machine-contract`: SM construction changes from interface + composition (`new TransportIo(...)`) to base class inheritance (`TcpStateMachineBase<TOps>`). Lifecycle dispatch, read loop, write path, async result routing, and cleanup move to the base. Subclass contract narrows to: `Decode` callback, protocol-specific `OnRequest`/`OnResponse`, and body pump integration.
- `streams-stages-pipeline`: Stage logic loses `BridgeTransportData`, `BridgeFlushCompleted/Failed`, and data-item handling in `_outboundQueue`. `OnOutbound` becomes lifecycle-command-only for TCP. Stage logic target: ~80 lines (the original Phase D goal).

## Impact

- **GaudiHTTP Protocol SMs** (`src/GaudiHTTP/Protocol/Syntax/`): All six TCP SMs (H10 client/server, H11 client/server, H2 client/server) refactored to extend the new base class. Encode methods change from `WireBuffer.Rent` → `OnOutbound` to `IBufferWriter<byte>` / `GetMemory` → `Flush`.
- **GaudiHTTP Stage Logic** (`src/GaudiHTTP/Streams/Stages/`): `HttpClientConnectionStageLogic` and `HttpServerConnectionStageLogic` lose the bridge, `BridgeFlush*` message handling, and `TransportData` items from `_outboundQueue`/`PostStop`.
- **GaudiHTTP Protocol** (`src/GaudiHTTP/Protocol/`): `TransportIo` refactored into `TcpStateMachineBase`. `PipeIoMessages.cs` loses `BridgeFlushCompleted`/`BridgeFlushFailed`. `IClientStageOperations.OnOutbound`/`IServerStageOperations.OnOutbound` signature unchanged but TCP call sites narrowed to lifecycle commands only.
- **Test suites**: SM unit tests change constructor from `new XxxStateMachine(options, ops)` to subclass construction with base class. `FakeClientOps`/`FakeServerOps` `OnOutbound` assertions change — no more `TransportData` items to assert. Stage-level tests remove bridge-specific assertions.
- **QUIC/H3**: Explicitly out of scope — H3 SMs do not use `TransportIo` for outbound (they use `MultiplexedData`), and the `TransportData` class itself is retained for QUIC.
