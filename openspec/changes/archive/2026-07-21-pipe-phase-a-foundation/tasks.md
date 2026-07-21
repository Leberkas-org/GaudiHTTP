## 1. Interface + Implementation (servus.akka)

- [x] 1.1 Define `IConnectionTransport` in `Servus.Akka.Transport` with `ReadAsync(CancellationToken)`,
      `AdvanceTo(SequencePosition)`, `AdvanceTo(SequencePosition, SequencePosition)`, `GetMemory(int)`,
      `Advance(int)`, `FlushAsync(CancellationToken)`, `CompleteOutput()`, `Info` (`ConnectionInfo`),
      `Abort()`
- [x] 1.2 Implement `ConnectionTransport` wrapping a `PipeReader` (inbound) + `PipeWriter` (outbound) +
      `ConnectionInfo`, with `Abort()` cancelling in-flight operations and completing both pipes
- [x] 1.3 Add pipe threshold options (input pause/resume, output pause/resume, segment size) scoped under
      `TcpTransportOptions`, with defaults 128KB/64KB (input) and 256KB/128KB (output)
- [x] 1.4 Add a small factory/helper that builds the input+output `Pipe` pair from the threshold options
      (used by tests and, later, by Phase D's transport-stage wiring)

## 2. Pump Implementation (servus.akka)

- [x] 2.1 Implement `TransportPumps.RunReadPump(Socket, PipeWriter, AdaptiveHint, CancellationToken)` —
      `GetMemory` → `ReceiveAsync` → `Advance` → adapt hint → `FlushAsync` loop; `Complete()` on EOF,
      `Complete(ex)` on error
- [x] 2.2 Implement `TransportPumps.RunWritePump(PipeReader, Socket, CancellationToken)` — `ReadAsync` →
      per-segment `SendAsync` → `AdvanceTo(Buffer.End)` loop; `Complete()` on completed+empty result,
      `Complete(ex)` on error
- [x] 2.3 Verify both pumps handle cancellation (token cancelled mid-await) without leaking unobserved
      task exceptions

## 3. TransportConnected Extension (servus.akka)

- [x] 3.1 Add optional `IConnectionTransport? Transport = null` member to `TransportConnected` in
      `ITransportInbound.cs`
- [x] 3.2 Confirm existing single-argument call sites (`new TransportConnected(info)`) compile unchanged
      — no other code in servus.akka or GaudiHTTP needs to change in this phase

## 4. Tests (servus.akka)

- [x] 4.1 `ConnectionTransport` unit tests: `ReadAsync` returns buffered data, `AdvanceTo` releases
      segments, `GetMemory`/`Advance` commit without flush-visibility, `FlushAsync` sync-below/async-at
      threshold, `CompleteOutput` propagates completion, `Abort` tears down both pipes, post-completion
      `ReadAsync`/`FlushAsync` report `IsCompleted = true`
- [x] 4.2 Read pump unit tests: data delivery, EOF, socket error, cancellation, inbound backpressure
      (slow consumer pauses the pump), adaptive hint growth/shrink
- [x] 4.3 Write pump unit tests: data delivery (multi-segment), completion drain, socket error,
      cancellation
- [x] 4.4 Pipe threshold options tests: defaults applied when unconfigured, custom thresholds/segment size
      honored in constructed `PipeOptions`
- [x] 4.5 Run `dotnet run --project <servus.akka test project>` (or the relevant filtered class) to
      confirm all new tests are green and nothing else regressed

## 5. Verification

- [x] 5.1 Confirm zero GaudiHTTP files touched (this phase is servus.akka-only)
- [x] 5.2 Confirm `TcpConnectionStage`/`TcpConnectionStateMachine` behavior is unchanged (no references to
      the new types added there yet — that's Phase D, `pipe-phase-d-wiring`)
- [x] 5.3 Roslyn navigator check: no unexpected new callers of `IConnectionTransport`/`ConnectionTransport`
      outside the new files and their tests
