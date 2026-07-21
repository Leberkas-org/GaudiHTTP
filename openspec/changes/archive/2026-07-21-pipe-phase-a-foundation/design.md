## Context

This is Phase A of the `pipe-transport-tcp` master plan (see
`openspec/changes/pipe-transport-tcp/design.md` for full context and end-state rationale). Phase A's job
is narrow: land the `IConnectionTransport` abstraction and its pipe/pump machinery in `servus.akka`,
fully tested, without touching any consumer. Nothing in this phase changes observable behavior of the
existing TCP transport or GaudiHTTP — it only adds new, currently-unused code paths.

Splitting the master plan into phases keeps each PR reviewable and independently revertable. Phase A is
deliberately inert: `IConnectionTransport`/`ConnectionTransport`/the pumps compile and are unit-tested,
but `TcpConnectionStateMachine` keeps using `WireBuffer` + `Channel<WireBuffer>` exactly as today.

## Goals / Non-Goals

**Goals:**
- Introduce `IConnectionTransport` as a method-based (not property-based) wrapper over `PipeReader` +
  `PipeWriter`, matching the contract already agreed in the master design.
- Implement `ConnectionTransport` and the two pump static methods, independently testable with fake
  sockets/pipes.
- Make pipe thresholds configurable via transport options, with sane defaults.
- Extend `TransportConnected` additively so later phases have a landing spot, without changing any
  existing call site.

**Non-Goals (deferred to later phases):**
- Wiring `IConnectionTransport` into `TcpConnectionStage/TcpConnectionStateMachine` (Phase D).
- Removing `WireBuffer`, `Channel<WireBuffer>`, `TransportDataFlushed`, or watermark tracking (Phase D/E).
- `FrameDecoder` / encoder changes (Phase B / later).
- Protocol state machine transport-I/O ownership changes (Phase C).
- Adaptive pipe thresholds or BDP-based budgets — fixed, configurable thresholds only.

## Decisions

### Decision 1: IConnectionTransport is method-based, not property-based

Same rationale as the master design: exposing raw `PipeReader`/`PipeWriter` would let a future protocol
consumer call `PipeReader.Complete()` or `PipeWriter.CancelPendingFlush()` directly, leaking lifecycle
control that must stay with the transport. Method wrapping also gives a natural seam for tracing/metrics
and for a test double that returns scripted `ReadResult`/`FlushResult` values without a real `Pipe`.

```
IConnectionTransport
  ReadAsync(CancellationToken) → ValueTask<ReadResult>
  AdvanceTo(SequencePosition consumed)
  AdvanceTo(SequencePosition consumed, SequencePosition examined)
  GetMemory(int sizeHint = 0) → Memory<byte>
  Advance(int bytes)
  FlushAsync(CancellationToken) → ValueTask<FlushResult>
  CompleteOutput()
  ConnectionInfo Info { get; }
  Abort()
```

`ConnectionTransport` is the sole implementation for Phase A, constructed from a `Pipe` (input), a `Pipe`
(output), and a `ConnectionInfo`. It stores a `CancellationTokenSource` so `Abort()` can cancel in-flight
`ReadAsync`/`FlushAsync` calls and complete both pipes with a cancellation exception, per the master
spec's `Abort` scenario.

### Decision 2: Pumps are static methods, not instance methods on ConnectionTransport

`RunReadPump`/`RunWritePump` take a `Socket` plus the specific `PipeWriter`/`PipeReader` they drive —
they do not need `IConnectionTransport` at all, since a pump only ever touches one side of one pipe. Static
methods keep them trivially unit-testable (pass a fake/loopback socket and a real `Pipe`'s reader/writer)
without needing to construct a full `ConnectionTransport` or stand up the transport stage. This mirrors how
the master design's Decision 10/11 pseudocode is already free-standing.

```
static class TransportPumps
{
    static Task RunReadPump(Socket socket, PipeWriter writer, AdaptiveHint hint, CancellationToken ct);
    static Task RunWritePump(PipeReader reader, Socket socket, CancellationToken ct);
}
```

Read pump loop: `GetMemory(hint.Current)` → `socket.ReceiveAsync` → `Advance(n)` → adapt hint →
`FlushAsync` → repeat; `n == 0` or cancellation → `writer.Complete()`; exception → `writer.Complete(ex)`.

Write pump loop: `reader.ReadAsync` → for each segment in `result.Buffer`, `socket.SendAsync` → 
`AdvanceTo(result.Buffer.End)` → repeat until `result.IsCompleted && result.Buffer.IsEmpty`; exception →
`reader.Complete(ex)`; normal termination → `reader.Complete()`.

Both pumps are `internal` (or `public` under `Servus.Akka.Transport`, TBD at implementation time to match
existing visibility conventions in the folder) — Phase A does not require them to be part of any public
contract since nothing outside the pump unit tests calls them yet.

### Decision 3: TransportConnected carries transport as an additive optional member

```csharp
public sealed record TransportConnected(ConnectionInfo Info, IConnectionTransport? Transport = null)
    : ITransportInbound;
```

Adding an optional trailing parameter with a default is source- and binary-compatible with existing
`TransportConnected(info)` call sites (records support this via the primary constructor). No existing
caller needs to change in this phase. Phase D is what actually populates `Transport` from
`TcpConnectionStateMachine` and starts relying on it.

### Decision 4: Pipe threshold options

Same defaults as the master design, added to `TransportOptions` (or a TCP-specific options record — see
open question below) so they're configurable per the master spec's requirement:

```
Input pipe (inbound):   pauseWriterThreshold 128 KB / resumeWriterThreshold 64 KB
Output pipe (outbound): pauseWriterThreshold 256 KB / resumeWriterThreshold 128 KB
Segment size: configurable, default per PipeOptions (4 KB) — callers that expect large frames
              (H2/H3, added in later GaudiHTTP phases) can override via minimumSegmentSize
```

These options are read by `ConnectionTransport`'s construction path (or a small factory helper) to build
the two `Pipe` instances via `PipeOptions`. Because nothing constructs a `ConnectionTransport` in the live
transport path yet, this phase's tests exercise the options by constructing `Pipe`/`PipeOptions` directly
and asserting threshold behavior (data flush going async above the pause threshold, resuming below the
resume threshold).

**Open question for implementation**: whether these thresholds live on the shared `TransportOptions` base
(visible to QUIC too, unused there) or a new `TcpPipeOptions` nested under `TcpTransportOptions`. Given
`TcpTransportOptions : TransportOptions` already carries TCP-only members (`AutoReconnect`, `UseProxy`),
prefer adding a `PipeThresholds` (or similarly named) options record scoped to `TcpTransportOptions` to
avoid polluting the QUIC path.

## Risks / Trade-offs

**[Building an abstraction with no consumer]** → Phase A code cannot be exercised end-to-end (no
integration/E2E coverage) until Phase D wires it in. Mitigation: thorough unit tests against real
`Pipe`/loopback-socket pairs give confidence in the pumps' and `ConnectionTransport`'s correctness in
isolation; Phase D adds the integration coverage once it's live.

**[Threshold defaults not yet validated under real load]** → The 128KB/64KB and 256KB/128KB defaults come
from the master design's estimate, not from Phase A benchmarking (nothing to benchmark yet — no live
path). Mitigation: values are configurable, not hard-coded, so Phase D/E can tune them once the pipes
carry real traffic without another interface change.

**[Divergent visibility/placement choices vs. later phases]** → If Phase A places pump methods or options
in a way Phase D's wiring finds awkward, some churn is possible. Mitigation: keep the surface minimal and
mirror the master design's pseudocode signatures exactly, so Phase D's job is to call these methods, not
redesign them.
