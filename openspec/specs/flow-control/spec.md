# Flow Control

Inbound and outbound flow control for HTTP/2 and HTTP/3 body data. Covers the `IFlowController<T>` abstraction, the H2 `FlowController` (RFC 9113 connection+stream windows), H3/QUIC byte-credit backpressure, adaptive window scaling, and the pump-level outbound budget that gates body emission on real transport flushes.

Scope: `GaudiHTTP.Protocol.Multiplexed` (shared abstractions), `GaudiHTTP.Protocol.Syntax.Http2.FlowController` / `WindowScaler` / `RttEstimator`, `GaudiHTTP.Protocol.Syntax.Http3.Http3OutboundWriter` (outbound budget), and `GaudiHTTP.Protocol.Body` pump integration (`FlowControlledBodyPump`, `MultiplexedBodyPump`, `SerialBodyPump`). Excludes body pump lifecycle internals (covered by body-handling spec), frame encoding/decoding details, and OS/library-level QUIC transport flow control.

## Requirements

### Requirement: IFlowController<T> contract

`IFlowController<T>` is the protocol-agnostic abstraction for multiplexed flow control. It is generic over the stream identifier type (`int` for H2 stream IDs, `long` for H3/QUIC stream IDs). Both client and server session managers consume it to gate outbound DATA emission and to account for inbound DATA reception.

#### Scenario: GetSendWindow returns the effective send budget for a stream
- **WHEN** `GetSendWindow(streamId)` is called
- **THEN** it returns the minimum of the connection-level send window and the per-stream send window, floored at zero

#### Scenario: OnDataSent debits both connection and stream send windows
- **WHEN** `OnDataSent(streamId, length)` is called after emitting a DATA frame
- **THEN** the connection send window is decremented by `length` AND the stream's send window is decremented by `length`

#### Scenario: OnSendWindowUpdate credits the send window
- **WHEN** `OnSendWindowUpdate(streamId, increment)` is called with `streamId == 0`
- **THEN** the connection-level send window is incremented
- **WHEN** called with a non-zero stream ID
- **THEN** only that stream's send window is incremented

#### Scenario: OnSendWindowUpdate rejects overflow beyond int.MaxValue
- **WHEN** a WINDOW_UPDATE would cause the connection or stream send window to exceed `int.MaxValue`
- **THEN** `HttpProtocolException` is thrown (RFC 9113 section 6.9.1 violation)

#### Scenario: OnInboundData accounts received bytes and emits WINDOW_UPDATEs
- **WHEN** `OnInboundData(streamId, dataLength)` is called
- **THEN** the connection receive window and stream receive window are decremented by `dataLength`
- **AND** the result indicates `Success = true` when both windows remain non-negative
- **AND** pending increments are accumulated until the threshold is reached, then emitted as `WindowUpdateSignal<T>` values

#### Scenario: OnInboundData detects connection-level violation
- **WHEN** `OnInboundData` causes the connection receive window to go negative
- **THEN** the result has `Success = false` and `IsConnectionViolation = true`

#### Scenario: OnInboundData detects stream-level violation
- **WHEN** `OnInboundData` causes a stream's receive window to go negative (but the connection window is still non-negative)
- **THEN** the result has `Success = false`, `IsStreamViolation = true`, and `ViolationStreamId` identifies the offending stream

#### Scenario: InitStreamSendWindow seeds a new stream's send window
- **WHEN** `InitStreamSendWindow(streamId)` is called for a newly opened stream
- **THEN** the stream's send window is set to the current initial send window size (from the peer's SETTINGS)

#### Scenario: RemoveStreamSendWindow cleans up a closed stream
- **WHEN** `RemoveStreamSendWindow(streamId)` is called
- **THEN** the stream's send window tracking is removed from internal state

#### Scenario: ApplyInitialWindowSizeDelta adjusts all active stream windows
- **WHEN** `ApplyInitialWindowSizeDelta(delta)` is called (from a SETTINGS change)
- **THEN** the initial send window baseline is adjusted by `delta` AND every currently tracked stream's send window is adjusted by the same `delta`

#### Scenario: OnStreamClosed flushes pending receive increments
- **WHEN** `OnStreamClosed(streamId)` is called and the stream has accumulated pending WINDOW_UPDATE increments
- **THEN** a `WindowUpdateSignal<T>` is returned so the caller can emit a final WINDOW_UPDATE for the stream
- **AND** all internal tracking for the stream (receive window, send window, pending increments) is removed

#### Scenario: OnGoAway marks the controller as draining
- **WHEN** `OnGoAway()` is called
- **THEN** `GoAwayReceived` returns `true`, signaling that no new streams should be opened

#### Scenario: Reset restores virgin state for reconnection
- **WHEN** `Reset(connectionWindowSize, streamWindowSize)` is called
- **THEN** all windows, pending increments, stream tracking, and GoAway state are cleared
- **AND** connection send window is reset to the HTTP/2 default (65535 bytes)
- **AND** the initial send stream window is reset to 65535 bytes

#### Scenario: OnRemoteSettings processes SETTINGS_INITIAL_WINDOW_SIZE
- **WHEN** `OnRemoteSettings` receives a SETTINGS frame containing `INITIAL_WINDOW_SIZE`
- **THEN** `ApplyInitialWindowSizeDelta` is called with the difference between the new and old values
- **AND** the result carries the new value in `InitialWindowSizeChange`

#### Scenario: OnRemoteSettings ignores ACK frames
- **WHEN** `OnRemoteSettings` receives a SETTINGS ACK
- **THEN** a default (empty) `SettingsResult` is returned and no state is modified

### Requirement: H2 FlowController dual-window model

The HTTP/2 `FlowController` implements `IFlowController<int>` with the RFC 9113 dual-window model: one connection-level window shared by all streams, plus one per-stream window. Both inbound (receive) and outbound (send) directions are tracked independently.

#### Scenario: Connection send window starts at HTTP/2 default
- **WHEN** a `FlowController` is constructed
- **THEN** the connection send window is initialized to 65535 bytes (RFC 9113 section 6.9.2)
- **AND** the initial per-stream send window is also 65535 bytes

#### Scenario: Receive windows are configurable at construction
- **WHEN** a `FlowController` is constructed with `connectionWindowSize` and `streamWindowSize`
- **THEN** the connection receive window is set to `connectionWindowSize`
- **AND** the initial per-stream receive window is set to `streamWindowSize`

#### Scenario: WINDOW_UPDATE threshold is one quarter of stream window with a minimum
- **WHEN** a `FlowController` is constructed with a `streamWindowSize`
- **THEN** the WINDOW_UPDATE emission threshold is `max(8 * 1024, streamWindowSize / 4)`
- **AND** WINDOW_UPDATEs are batched until the accumulated pending increment reaches the threshold

#### Scenario: Connection WINDOW_UPDATE is emitted independently from stream WINDOW_UPDATE
- **WHEN** inbound data accumulates pending increments
- **THEN** a connection WINDOW_UPDATE (`streamId = 0`) is emitted when the connection pending increment reaches the threshold
- **AND** a stream WINDOW_UPDATE is emitted when that specific stream's pending increment reaches the threshold
- **AND** these two signals are independent -- one may fire without the other

#### Scenario: Reserve pre-debits send windows before a body read
- **WHEN** `Reserve(streamId, amount)` is called by `FlowControlledBodyPump` before starting a body read
- **THEN** both the connection and stream send windows are decremented by `amount`
- **AND** the reserved amount is tracked per-slot so it can be refunded if the actual read is shorter

#### Scenario: Refund restores unused reserved send window
- **WHEN** `Refund(streamId, amount)` is called after a body read returns fewer bytes than reserved
- **THEN** both the connection and stream send windows are incremented by `amount`
- **AND** if `amount <= 0` the call is a no-op

#### Scenario: OnPing echoes non-ACK pings
- **WHEN** `OnPing` receives a PING frame that is not an ACK
- **THEN** a PING ACK with the same opaque data is returned
- **WHEN** `OnPing` receives a PING ACK
- **THEN** `null` is returned (the ACK is consumed, not echoed)

### Requirement: H2 adaptive window scaling

The `WindowScaler` and `RttEstimator` cooperate to grow the per-stream receive window based on the measured bandwidth-delay product (BDP), matching the heuristic used by `SocketsHttpHandler`. Scaling is optional -- when no `WindowScaler` is provided, windows remain static.

#### Scenario: WindowScaler doubles the window when BDP exceeds the threshold
- **WHEN** `ComputeNewWindow` is called and `deliveredBytes * minRtt > currentWindow * elapsed * multiplier`
- **THEN** the returned window is `min(maxWindow, currentWindow * 2)`

#### Scenario: WindowScaler caps growth at maxWindow
- **WHEN** the current window already equals or exceeds `maxWindow`
- **THEN** `ComputeNewWindow` returns `currentWindow` unchanged

#### Scenario: WindowScaler returns unchanged window for degenerate inputs
- **WHEN** `minRtt <= 0`, `elapsed <= 0`, or `deliveredBytes <= 0`
- **THEN** `ComputeNewWindow` returns `currentWindow` unchanged

#### Scenario: RttEstimator tracks minimum RTT
- **WHEN** a measurement PING is sent and its ACK arrives
- **THEN** the round-trip time is measured via `TimeProvider`
- **AND** `MinRtt` is updated to the smallest RTT observed so far

#### Scenario: RttEstimator gates ping frequency
- **WHEN** `ShouldSendPing()` is called and the previous measurement was sent less than 100ms ago
- **THEN** it returns `false`
- **WHEN** no measurement is in flight and the interval has elapsed
- **THEN** it returns `true`

#### Scenario: WINDOW_UPDATE threshold does not grow with scaled window
- **WHEN** adaptive scaling grows the per-stream receive window
- **THEN** the WINDOW_UPDATE emission threshold stays at its original value (`max(8 * 1024, initialStreamWindow / 4)`)
- **AND** this prevents deadlocking new streams whose server send window matches the original (unscaled) advertised SETTINGS_INITIAL_WINDOW_SIZE

#### Scenario: Window growth is applied as an increment on the next WINDOW_UPDATE
- **WHEN** scaling decides to grow from `oldWindow` to `newWindow`
- **THEN** the pending stream WINDOW_UPDATE increment is increased by `newWindow - oldWindow`
- **AND** subsequent streams inherit the grown `_initialRecvStreamWindow` for their initial accounting

#### Scenario: Reset clears adaptive scaling state
- **WHEN** `Reset` is called on the FlowController
- **THEN** the RTT estimator is reset (MinRtt = 0, no pending measurement)
- **AND** delivered-bytes samples and timestamps for all streams are cleared
- **AND** the initial receive window and threshold are recalculated from the new parameters

### Requirement: H3/QUIC outbound byte-credit model

HTTP/3 does not have application-level flow control windows (QUIC handles transport-level flow control). Instead, GaudiHTTP uses a per-stream outbound byte budget (`OutboundBodyByteBudget = 256 * 1024`) enforced by the `MultiplexedBodyPump` via the `Http3OutboundWriter`. This budget limits how many body bytes are in flight (emitted but not yet flushed to the wire) per stream, preventing unbounded memory growth under concurrent uploads.

#### Scenario: Per-stream budget is seeded at registration
- **WHEN** a stream's body is registered with the `MultiplexedBodyPump`
- **THEN** `slot.AvailableBytes` is initialized to `maxBytesPerStream` (256 KB for H3)

#### Scenario: Body reads debit the per-stream budget
- **WHEN** a body chunk of `bytesRead` bytes is emitted as a DATA frame
- **THEN** `slot.AvailableBytes` is decremented by `bytesRead`

#### Scenario: Depleted budget parks only the affected stream
- **WHEN** a stream's `AvailableBytes` reaches zero or below
- **THEN** that stream is removed from the ready queue and not scheduled for further reads
- **AND** sibling streams with remaining budget continue draining independently

#### Scenario: MultiplexedDataFlushed credits the flushed stream
- **WHEN** the transport reports a `MultiplexedDataFlushed` for a specific QUIC stream
- **THEN** `OnCapacityAvailable(streamId, bytes)` is called on the pump
- **AND** the stream's `AvailableBytes` is incremented by the flushed bytes, clamped to `maxBytesPerStream`
- **AND** if the stream was parked (budget depleted) and not already queued or reading, it is re-enqueued for scheduling

#### Scenario: MultiplexedDataFlushed for an unknown stream is a safe no-op
- **WHEN** `OnCapacityAvailable` is called with a stream ID not present in `_activeSlots`
- **THEN** the call returns without error or side effects

#### Scenario: H3 does not use IFlowController
- **WHEN** the H3 client or server session manager processes body data
- **THEN** no `IFlowController` is consulted -- outbound gating is purely via the byte budget, and inbound flow control is delegated to the QUIC transport layer

### Requirement: FlowControlledBodyPump (H2 outbound gating)

`FlowControlledBodyPump` is the H2-specific body pump that integrates with `FlowController` to gate body reads on the available send window. It uses both connection-level and per-stream window availability to decide when to schedule reads and how many bytes to read.

#### Scenario: Registration checks window availability before enqueuing
- **WHEN** a stream's body is registered with the pump
- **THEN** the stream is enqueued for reading only if both its stream send window and the connection send window are positive
- **AND** if either window is zero, the stream is added to `_windowBlockedStreams` instead

#### Scenario: Read size is capped by available window
- **WHEN** a read is started for a stream
- **THEN** the read size is `min(chunkSize, streamSendWindow, connectionSendWindow)`
- **AND** the computed read size is pre-reserved via `flowController.Reserve(streamId, readSize)`

#### Scenario: Unused reservation is refunded after read completion
- **WHEN** a body read completes with `bytesRead < reservedWindow`
- **THEN** `flowController.Refund(streamId, reservedWindow - bytesRead)` restores the unused portion to both connection and stream windows

#### Scenario: Connection WINDOW_UPDATE unblocks all eligible streams
- **WHEN** `OnWindowUpdate` is called with `streamId == 0` (connection-level)
- **THEN** every stream in `_windowBlockedStreams` whose per-stream window is also sufficient (>= `chunkSize / 2`) is moved to the ready queue
- **AND** streams whose per-stream window is still insufficient remain blocked

#### Scenario: Stream WINDOW_UPDATE unblocks only that stream
- **WHEN** `OnWindowUpdate` is called with a non-zero stream ID
- **THEN** only that specific stream is removed from `_windowBlockedStreams` and enqueued

#### Scenario: Minimum read size prevents tiny reads
- **WHEN** the available window (min of connection and stream) is less than `chunkSize / 2`
- **THEN** the stream is moved to `_windowBlockedStreams` instead of being scheduled for a read

#### Scenario: Hard cap limits total concurrent in-flight reads
- **WHEN** `_asyncInFlight` reaches the lesser of `_readSlots` and `hardCap`
- **THEN** no further reads are started, even if streams are queued and have available window

### Requirement: SerialBodyPump outbound byte budget

`SerialBodyPump` (used by HTTP/1.1) applies a fixed outbound byte budget (`maxBytes`) to gate body reads, ensuring bounded memory even on serial connections. The credit model is the same principle as the multiplexed pump but applied to a single active stream.

#### Scenario: Budget is seeded at registration
- **WHEN** `Register` is called with a body stream
- **THEN** `_availableBytes` is set to `maxBytes`

#### Scenario: Body reads debit the budget
- **WHEN** a body chunk of `bytesRead` bytes is emitted
- **THEN** `_availableBytes` is decremented by `bytesRead`

#### Scenario: TransportDataFlushed credits the budget
- **WHEN** `OnCapacityAvailable(bytes)` is called after a real transport flush
- **THEN** `_availableBytes` is incremented by `bytes`, clamped to `maxBytes`
- **AND** if the pump was parked (budget <= 0), a new read is attempted

#### Scenario: ResetCredit restores full budget on reconnect
- **WHEN** `ResetCredit()` is called after a connection is re-established
- **THEN** `_availableBytes` is restored to `maxBytes`
- **AND** if a body stream is active, a read is attempted immediately

#### Scenario: Depleted budget prevents further reads
- **WHEN** `_availableBytes <= 0`
- **THEN** `TryStartRead` returns without scheduling a read, even if a body stream is active and no read is in flight

### Requirement: Reconnection and flow control state

Flow control state must be correctly reset during reconnection to prevent deadlocks (stuck pumps) or protocol violations (stale windows applied to a new connection).

#### Scenario: FlowController.Reset clears all window state
- **WHEN** `Reset` is called on the H2 `FlowController`
- **THEN** all per-stream receive and send windows are cleared
- **AND** pending WINDOW_UPDATE increments are zeroed
- **AND** connection send window is reset to 65535

#### Scenario: FlowControlledBodyPump.Cleanup handles in-flight reads safely
- **WHEN** `Cleanup` is called while reads are in flight
- **THEN** the connection CTS is cancelled (forcing outstanding reads to complete)
- **AND** in-flight slots are moved to `_draining` (not disposed) to prevent buffer use-after-free
- **AND** the generation counter is bumped so late completions from the old connection are identifiable as stale

#### Scenario: Stale read completions are dropped after reconnect
- **WHEN** a `HandleReadComplete` or `HandleReadFailed` arrives with a generation older than the current generation
- **THEN** the completion is matched against `_draining` and the orphaned slot is released
- **AND** the current connection's `_activeSlots` are never touched

#### Scenario: Reconnect does not refund stale reserved windows
- **WHEN** a draining slot from a previous generation completes
- **THEN** its `ReservedWindow` is NOT refunded to the (now-reset) FlowController -- the old connection's window accounting is dead

#### Scenario: MultiplexedBodyPump.Cleanup mirrors the FlowControlledBodyPump discipline
- **WHEN** `Cleanup` is called on the multiplexed pump
- **THEN** the same generation-bump, CTS-cancel, in-flight-to-draining, and fresh-CTS-swap sequence is applied
- **AND** a new incarnation's reads use the new CTS and current generation

### Requirement: FlowControlResult signals

`FlowControlResult<T>` is a value type that communicates the outcome of inbound data accounting back to the caller in a single return value, avoiding separate method calls for violation checks and WINDOW_UPDATE emission.

#### Scenario: Successful reception with no WINDOW_UPDATEs due
- **WHEN** inbound data is accounted and both windows remain non-negative, but pending increments have not reached the threshold
- **THEN** `Success = true`, `ConnectionWindowUpdate = null`, `StreamWindowUpdate = null`

#### Scenario: Successful reception with WINDOW_UPDATEs
- **WHEN** inbound data pushes either pending increment past the threshold
- **THEN** `Success = true` and the appropriate `WindowUpdateSignal<T>` fields are populated with the stream ID and increment amount

#### Scenario: Connection violation
- **WHEN** the connection receive window goes negative
- **THEN** `Success = false`, `IsConnectionViolation = true` -- the caller must send GOAWAY with FLOW_CONTROL_ERROR

#### Scenario: Stream violation
- **WHEN** a stream's receive window goes negative (connection still non-negative)
- **THEN** `Success = false`, `IsStreamViolation = true`, `ViolationStreamId` identifies the stream -- the caller must send RST_STREAM with FLOW_CONTROL_ERROR
