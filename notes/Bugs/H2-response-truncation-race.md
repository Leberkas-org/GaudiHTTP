---
status: fixed
component: Protocol/Body
discovered: '2026-06-11'
fixed: '2026-06-11'
branch: fix/stress-benchmarks
severity: high
tags:
  - bug
  - http2
  - race-condition
  - fixed
---
# H2 Body Truncation/Corruption Race — FIXED (2026-06-11)

## Root cause

`QueuedBodyReader` (`src/TurboHttp/Protocol/Body/QueuedBodyReader.cs`) — the ring-buffer
queue between a connection-stage (actor) thread producing body chunks (`TryEnqueue`/`Complete`)
and the application thread consuming them (`ReadAsync`/`AdvanceTo`) — had **no synchronization
at all**. The codebase's "actor confinement makes plain fields safe" convention does not apply
here: this type is a true cross-thread boundary. It is used for HTTP/2 server request bodies,
HTTP/2 client response bodies, HTTP/3 (both sides), and HTTP/1.x streamed bodies — which is why
both directions failed symmetrically.

The three observed failure modes mapped to specific interleavings:

| Symptom | Interleaving |
|---------|--------------|
| Whole chunk lost → body short by N×16384, surfaced as HTTP 200 | non-atomic `_count++`/`_count--` race loses an increment; `Complete()` sees `_count == 0` and reports clean end-of-body |
| Adjacent chunks reordered | consumer reads stale `_count == 0`, sets `_readPending`; producer's next chunk is delivered directly via `SetResult`, bypassing the older queued chunk |
| Corrupted payload at correct length | lost decrement → consumer re-reads a stale slot over a returned `ArrayPool` array |

**It was never a flow-control, frame-encoding, coalescing, or transport bug.** Frame-level
tracing (permanent `DATA in/out` Trace instrumentation added to both session managers) proved
client-out == server-in byte-for-byte; the bytes died between `HandleDataFrame`/`FeedBody`
and the body stream consumer.

## The fix

- `QueuedBodyReader`: all mutable state guarded by a private lock; completion delivery
  (`SetResult`/`SetException`) claimed atomically (`_readPending` cleared under the lock —
  only one of TryEnqueue/Complete/Fault/cancellation/Reset can deliver) and invoked outside
  the lock; `ManualResetValueTaskSourceCore.RunContinuationsAsynchronously = true` so consumer
  continuations never run on the connection-stage thread; `_core.Reset()` ordered before
  `_readPending` publication.
- Secondary fix (client correctness): `StreamState.ExpectedBodyLength` (H2) — END_STREAM
  arriving with a byte count != declared Content-Length now faults the body reader with
  `HttpRequestException` instead of completing it (skipped for HEAD/204/304). RFC 9113 §8.1.1.
  Note: H3 has its own `StreamState`; the same guard is NOT yet wired there.

## Tests

- `TurboHttp.Tests/Protocol/Body/QueuedBodyReaderConcurrencySpec.cs` — producer/consumer
  hammer with position-derived byte pattern; failed within 1 round pre-fix (lost 39 KB),
  green 5×/5× post-fix.
- `TurboHttp.Tests/.../Http2StreamStateBodyTruncationSpec.cs` — 5 specs for the
  Content-Length guard (RFC9113-8.1.1 trait).
- `TurboHTTP.IntegrationTests.End2End/H2/PatternedPayloadIntegritySpec.cs` — permanent
  regression spec (h2c, 20×512KB, patterned payloads with per-block provenance analysis in
  failure messages).

## Verification

- Pre-fix: `ConcurrentLargePostSpec` / patterned diagnostic failed ~1 in 5–12 iterations.
- Post-fix: **0 failures in 50+50 iterations** of both repro specs, plus 20 more of the
  regression spec; full suites green: unit 5571/5571, End2End 88/88, Server 89/89,
  Client 472 (0 failed).
