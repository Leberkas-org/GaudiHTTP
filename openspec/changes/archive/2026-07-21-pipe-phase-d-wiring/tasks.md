## 1. Transport Stage Pipe Mode Activation (servus.akka)

- [x] 1.1 Modify `TcpConnectionStateMachine.OnLeaseAcquired` to create an input+output `Pipe` pair, start
  `TransportPumps.RunReadPump`/`RunWritePump`, wrap them in a `ConnectionTransport`, and push
  `TransportConnected(IConnectionTransport)`
- [x] 1.2 Remove `Channel<WireBuffer>` outbound path, `OnFlushed` callback, `SendFlushed` event, and
  `_bytesInFlight`/watermark tracking from `TcpConnectionStateMachine`
- [x] 1.3 Remove inbound `ReceiveAsync` + `PipeTo(ReadCompleted)` path from `TcpConnectionStateMachine` —
  the read pump replaces it
- [x] 1.4 Update `TcpConnectionStage` to stop pushing `TransportData`/`TransportDataFlushed` on the TCP
  network port — lifecycle events only
- [x] 1.5 Unit test: `OnLeaseAcquired` creates exactly one pipe pair + two pump tasks, pushes
  `TransportConnected` with a non-null `IConnectionTransport`

## 2. Pump Monitoring (servus.akka)

- [x] 2.1 Add `PipeTo(Self)` monitoring for both pump tasks, tagged with the connection generation
- [x] 2.2 On pump completion/failure: complete the counterpart pipe, abort the transport, push
  `TransportDisconnected`
- [x] 2.3 Unit test: read-pump EOF triggers `TransportDisconnected`; write-pump socket error triggers
  `TransportDisconnected`; stale-generation pump completion after reconnect is dropped

## 3. Reconnect With Pipes (servus.akka)

- [x] 3.1 On reconnect: complete the old pipe pair (idempotent if pumps already unwound), create a new
  pipe pair + pump tasks + `ConnectionTransport`, increment generation, push new `TransportConnected`
- [x] 3.2 Unit test: reconnect produces a transport distinct from the pre-reconnect one; old transport's
  `ReadAsync`/`FlushAsync` observably fail/complete after reconnect

## 4. Remove SM Dual-Path Fallback (GaudiHTTP)

- [x] 4.1 Delete the legacy `TransportData`-decode fallback from each TCP client SM (H1.0, H1.1, H2) left
  in place by Phase C — SMs consume `IConnectionTransport` exclusively
- [x] 4.2 Delete the same fallback from each TCP server SM (H1.0, H1.1, H2)
- [x] 4.3 Remove now-dead `TransportData` member handling from `IClientStageOperations`/
  `IServerStageOperations` implementations where it only existed to serve the fallback
- [x] 4.4 Compile + unit test check after removal (no references to the fallback path remain)

## 5. Client Stage Logic Thinning (GaudiHTTP)

- [x] 5.1 Refactor `HttpClientConnectionStageLogic`: `onPush(_inNetwork)` calls
  `_sm.OnTransportEvent(item)` only, no `TransportData` branch
- [x] 5.2 Route all StageActor messages to `_sm.OnAsyncResult(msg)` unconditionally
- [x] 5.3 Remove `_outboundQueue`/`_responseQueue` data-item handling that existed only for the
  `TransportData` push/queue path (response queueing for `OutResponse` backpressure stays — only the
  network-data queueing goes)
- [x] 5.4 Update stage-level tests to assert lifecycle-only port behavior (assert no `TransportData` is
  ever grabbed/pushed on the TCP path)

## 6. Server Stage Logic Thinning + Completion (GaudiHTTP)

- [x] 6.1 Refactor `HttpServerConnectionStageLogic`: same thinning as client (port → `OnTransportEvent`,
  StageActor → `OnAsyncResult`)
- [x] 6.2 Adapt `CompleteAfterFlushingOutbound` to call `_transport.CompleteOutput()` instead of draining
  `_outboundQueue`; complete the stage once the write-pump completion is observed via `OnAsyncResult`
- [x] 6.3 Keep `BodyResumed`/`MaxConcurrentRequests` dispatch gating unchanged (not part of this thinning)
- [x] 6.4 Update stage-level tests: completion-after-GOAWAY test asserts `CompleteOutput()` is called and
  `CompleteStage()` only fires after pump completion, not synchronously

## 7. Bug Fix Cherry-Picks (GaudiHTTP)

- [x] 7.1 Cherry-pick and adapt PendingRequest stale-cancel fix from `feat/outbound-flow-control` onto the
  pipe-based reconnect flow (Decision 5 in design.md)
- [x] 7.2 Cherry-pick and adapt reconnect body truncation fix
- [x] 7.3 Cherry-pick and adapt body-pump in-flight teardown fix
- [x] 7.4 Regression test for each of the three fixes against the pipe-based transport (not the old
  `WireBuffer` path)

## 8. Client Outbound Deferred Encoding (GaudiHTTP)

Phase D removed the inbound `TransportData` fallback but left the outbound side: all three client
SMs encode the first request immediately in `OnRequest`/`EncodeRequest`, before the TCP connection
is established (`Transport` is still null). The encoded bytes fall through to
`Ops.OnOutbound(TransportData)` which the pipe-based TCP stage ignores — the request is lost.

Fix: each client SM must defer request encoding until `OnTransportConnected` fires (Transport is
set). The reconnect-replay logic in `OnConnectionRestored` already handles re-encoding after
reconnect; the gap is the **initial connect** path only.

- [x] 8.1 H1.0 client: in `EncodeRequest`, return after sending `ConnectTransport` (request is
  already stored in `_inFlightRequest`). In `OnConnectionRestored`, when `!wasReconnecting` and
  `_inFlightRequest` is set, call `WriteRequest(_inFlightRequest)` to encode into the now-available
  pipe transport
- [x] 8.2 H1.1 client: in `OnRequest`, return after sending `ConnectTransport` (request is already
  enqueued in `_inFlightQueue`). In `OnConnectionRestored`, when `!wasReconnecting`, iterate
  `_inFlightQueue` and call `WriteRequest(pending)` for each
- [x] 8.3 H2 client: in `EncodeRequest`, when `EnsureConnected` returns true (just sent
  `ConnectTransport`), store the request and return without allocating a stream ID. In
  `OnConnectionRestored`, after sending the connection preface, flush the pending initial request
  via `EncodeRequest`
- [x] 8.4 H2 server negotiating SM: `PreStart` ordering already fixed — `Activate()` no longer
  calls `PreStart()` itself; callers invoke `PreStart` after `DecodeClientData(TransportConnected)`
  so Transport is set before SETTINGS emission (done in this session)
- [x] 8.5 Server negotiating SM sniff handoff: `AdvanceTo(buffer.Start)` instead of
  `AdvanceTo(buffer.Start, buffer.End)` so the inner SM's first `PipeReader.ReadAsync` resolves
  synchronously (done in this session)
- [x] 8.6 Unit test: verify that `EncodeRequest` with `Transport == null` does NOT emit
  `TransportData` on the outbound port; verify that `OnConnectionRestored` encodes the deferred
  request into the pipe transport

## 9. Full Suite Verification

- [x] 9.1 Run `dotnet run --project GaudiHTTP.Tests/GaudiHTTP.Tests.csproj` — unit+stage suite green
  (5994/5994)
- [ ] 9.2 Run `GaudiHTTP.IntegrationTests.Client` — green
- [ ] 9.3 Run `GaudiHTTP.IntegrationTests.End2End` — green
- [x] 9.4 Run `GaudiHTTP.IntegrationTests.Server` — green (89/89)
- [x] 9.5 Run servus.akka submodule test suite — green (~740 tests)
- [x] 9.6 Confirm no remaining references to `WireBuffer`/`Channel<WireBuffer>` on the TCP data path, and
  no remaining `TransportData` grab/push in either stage logic (grep verification)
