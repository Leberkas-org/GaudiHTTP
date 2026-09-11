## 1. Test Infrastructure

- [x] 1.1 Create `InMemoryTransport : IConnectionTransport` in `GaudiHTTP.Tests.Shared` with Feed/Complete (read), GetMemory/Advance/WrittenBytes/TakeWrittenBytes (write), configurable FlushMode
- [x] 1.2 Add `InMemoryTransport` unit tests verifying read delivery, write capture, AdvanceTo semantics, flush modes
- [x] 1.3 Add helper extension `ConnectTransport(sm, transport)` that dispatches `TransportConnected` + enqueues a pending read, reducing test boilerplate

## 2. H1.1 Write-Side

- [x] 2.1 `Http11ClientStateMachine.WriteRequest`: remove the `else` branch (WireBuffer.Rent + WireBufferWriter) — only the `TransportBufferWriter` path remains. Assert Transport is not null.
- [x] 2.2 `Http11ServerStateMachine.OnResponse`: replace `WireBuffer.Rent` + `WireBufferWriter` + `EmitWireBuffer` with `TransportBufferWriter(Transport)` for header encoding. Rewrite coalesced body to write sequentially into pipe.
- [x] 2.3 `Http11ServerStateMachine.EmitDataFrames` / `EmitOwnedDataFrames`: remove the `else` WireBuffer branch, keep only the `transport.GetMemory`/`Advance` path.
- [x] 2.4 `Http11ClientStateMachine.EmitDataFrames` / `EmitOwnedDataFrames`: remove the `else` WireBuffer branch.
- [x] 2.5 Delete `EmitWireBuffer` from `Http11ServerStateMachine` and `Http11ClientStateMachine`.
- [x] 2.6 Migrate H1.1 Server SM tests (15 files, 159 tests green) (`ServerStateMachineSpec`, `Http11ServerEncoderSpec`, `Http11ServerConnectionPersistenceSpec`, etc.) from `WireBuffer.Rent` + `ops.Outbound` assertions to `InMemoryTransport.Feed` + `transport.WrittenBytes`.
- [x] 2.7 Migrate H1.1 Client SM tests — BLOCKED: H1.1 Client SM does not extend TcpStateMachineBase, needs pipe-transport migration first (`Http11StateMachineSpec`, `Http11ClientEncoderSpec`, etc.) to `InMemoryTransport`.

## 3. H1.0 Write-Side

- [x] 3.1 `Http10ServerStateMachine`: replace `WireBuffer.Rent` + `WireBufferWriter` response encoding with `TransportBufferWriter(Transport)`. Remove `EmitDataFrames`/`EmitOwnedDataFrames` else branches. Delete `EmitWireBuffer` if present.
- [x] 3.2 `Http10ClientStateMachine`: TransportBufferWriter for request encoding, else branches removed: replace `WireBuffer.Rent` + `WireBufferWriter` request encoding with `TransportBufferWriter(Transport)`. Remove else branches.
- [x] 3.3 Migrate H1.0 Server SM tests to `InMemoryTransport` (72 tests green).
- [x] 3.4 Migrate H1.0 Client SM tests to `InMemoryTransport` (36 tests green).

## 4. H1 Read-Side

- [x] 4.1 `Http11ServerStateMachine.DecodeClientData`: remove `if (Transport is null && data is TransportData)` fallback. Add `InvalidOperationException` for unexpected `TransportData`.
- [x] 4.2 `Http11ClientStateMachine.DecodeServerData`: remove TransportData fallback. Add throw.
- [x] 4.3 `Http10ServerStateMachine.DecodeClientData`: remove TransportData fallback. Add throw.
- [x] 4.4 `Http10ClientStateMachine.DecodeServerData`: remove TransportData fallback. Add throw.
- [x] 4.5 Migrate remaining H1 tests that feed data via `DecodeClientData(TransportData.Rent(...))` to use `InMemoryTransport.Feed()` + `TryHandleAsyncResult(ReadCompleted(...))`.

## 5. H2 Write-Side

- [x] 5.1 Add reusable scratch buffer (`byte[]`) to `Http2ClientSessionManager` and `Http2ServerSessionManager` (start 64KB, grow on demand).
- [x] 5.2 Replace `EmitBuffer: Action<WireBuffer>` with `EmitData: EmitBytesDelegate` on both SessionManagers.
- [x] 5.3 Convert all `EmitWireBuffer(buf)` call sites in `Http2ClientSessionManager` to: serialize into scratch buffer, call `EmitData(scratch.AsSpan(0, written))`.
- [x] 5.4 Convert all `EmitWireBuffer(buf)` call sites in `Http2ServerSessionManager` to scratch buffer + `EmitData`.
- [x] 5.5 Convert `EmitFrame(Http2Frame)` in both SessionManagers to serialize into scratch buffer.
- [x] 5.6 Update `Http2ClientStateMachine` and `Http2ServerStateMachine`: replace `EmitWireBuffer` callback with `EmitData` that writes span into `Transport.GetMemory`/`Advance` + `RequestFlush()`. Delete `EmitWireBuffer`.
- [x] 5.7 Migrate H2 Client SM tests to `InMemoryTransport`.
- [x] 5.8 Migrate H2 Server SM tests to `InMemoryTransport`.

## 6. H2 Read-Side

- [x] 6.1 `Http2ClientStateMachine.DecodeServerData`: remove TransportData fallback. Add throw.
- [x] 6.2 `Http2ServerStateMachine.DecodeClientData`: remove TransportData fallback. Add throw.
- [x] 6.3 Migrate remaining H2 tests that feed data via `DecodeClientData`/`DecodeServerData` with `TransportData` to `InMemoryTransport`.

## 7. Body Pumps

- [x] 7.1 `SerialBodyPump` / `PumpSlot`: remove WireBuffer.Rent/Wrap paths where transport is available. Verify all EmitDataFrames call sites go through pipe.
- [x] 7.2 `BufferedBodyReader`: remove any WireBuffer.Rent paths that have transport alternatives.
- [x] 7.3 Migrate body pump tests that assert on WireBuffer-wrapped outbound data.

## 8. Cleanup

- [x] 8.1 Delete `WireBufferWriter` (`src/GaudiHTTP/Protocol/WireBufferWriter.cs`).
- [x] 8.2 Delete `WireBufferTestExtensions` from `GaudiHTTP.Tests/TestSupport/`.
- [x] 8.3 Remove WireBuffer imports from all modified TCP protocol files (verify no remaining usage via grep).
- [x] 8.4 Run full test suite (`dotnet run --project GaudiHTTP.Tests/GaudiHTTP.Tests.csproj`) and verify all pass.
- [x] 8.5 Run stage tests and integration tests to verify no regressions.
