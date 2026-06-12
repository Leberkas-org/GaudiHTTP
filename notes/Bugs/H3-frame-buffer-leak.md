---
status: fixed
component: Protocol/Http3
discovered: '2026-06-12'
fixed: '2026-06-12'
branch: release-next
severity: high
tags:
  - bug
  - http3
  - memory
  - pooling
  - fixed
---
# H3 Inbound Frame Buffer Leak — FIXED (2026-06-12)

## Symptom

Benchmark: TurboServer allocated **1.27 MB managed memory per 1 MB HTTP/3 upload** (Kestrel: 87 KB).
Client side showed the same pathology for H3 response bodies. Unit repro measured 1.04 bytes
allocated per body byte at steady state.

## Root cause

`FrameDecoder.DecodeDataFrame`/`DecodeHeadersFrame` (`Protocol/Syntax/Http3/FrameDecoder.cs`)
copy each frame payload into a `MemoryPool<byte>.Shared` rental owned by the frame
(`DataFrame`/`HeadersFrame` implement `IDisposable`). Neither consumer ever disposed the frames:

- Server: `Http3ServerSessionManager.ProcessFrames` — handled frames in a `foreach`/`switch`, no dispose.
- Client: `Http3ClientStateMachine.ProcessFrameData` — same.

Rentals were never returned → the array pool drained permanently → every subsequent
`Rent` allocated a fresh array → allocations ≈ full body size per request/response.

## The fix

1. Both frame loops dispose each frame in a per-frame `finally` after handling
   (handling copies what it keeps: body bytes via `QueuedBodyReader.TryEnqueue`,
   header strings via QPACK decode).
2. **Prerequisite**: `QpackTableSync.TryDecodeOrBlock` used to retain the *caller's*
   `ReadOnlyMemory<byte>` (aliasing the frame's pooled rental) in `_blockedStreams` —
   disposal would have corrupted blocked header blocks on pool reuse. It now stores an
   owned copy (`data.ToArray()`; blocked streams are rare and small).

## Tests

- `TurboHTTP.Tests/Protocol/Syntax/Http3/Server/SessionManager/Http3DataFrameBufferReleaseSpec.cs`
  — allocation-budget spec (thread-local `GC.GetAllocatedBytesForCurrentThread`, warmup + steady
  state, asserts < ¼ body size) + body round-trip integrity. Pre-fix: 4.35 MB for 4.19 MB body.
- `TurboHTTP.Tests/Protocol/Syntax/Http3/Client/StateMachine/Http3ResponseFrameBufferReleaseSpec.cs`
  — client-side equivalent. Pre-fix: 2.15 MB for 2.10 MB body.
- `TurboHTTP.Tests/Protocol/Syntax/Http3/Qpack/QpackBlockedStreamBufferOwnershipSpec.cs`
  — blocked header block must survive caller-buffer scribble.

## Lesson

Pooled-rent + copy paths must have an explicit owner with a deterministic dispose point.
The same audit found the H1.1 client pump issue (see [[H1.1-client-body-pump-backpressure]]).
A remaining (separate, optimization-level) issue: H3 still double-copies DATA payloads
(FrameDecoder rental → QueuedBodyReader rental); H2 avoids the first copy by slicing its
working buffer. Aligning H3 with H2 would cut another ~50% of transient copy traffic.
