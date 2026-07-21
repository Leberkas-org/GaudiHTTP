## Context

The master plan (`openspec/changes/pipe-transport-tcp/`) replaces the TCP transport with `System.IO.Pipelines` and, as Decision 6, changes `FrameDecoder.DecodeAll` to accept `ReadOnlySequence<byte>` so it can be handed a `PipeReader.ReadAsync` result directly, with unconsumed bytes retained by the Pipe via `AdvanceTo(consumed, examined)` instead of the decoder's own field-based remainder buffer.

That decoder signature/parsing change has no hard dependency on the transport rewrite itself — it only depends on the *shape* of the input (`ReadOnlySequence<byte>` vs `ReadOnlyMemory<byte>`), not on a real `Pipe` existing underneath. Phase B extracts this slice so it can be implemented, reviewed, and merged independently of Phase A, shrinking both diffs and letting them proceed in parallel.

## Goals / Non-Goals

**Goals:**
- Change `FrameDecoder.DecodeAll` (H2, H3) to `DecodeAll(in ReadOnlySequence<byte> input, out SequencePosition consumed)`
- Remove internal `_remainderBuffer`/`_remainder` fields; rewrite parsing against `SequenceReader<byte>`
- Preserve a zero-copy fast path for the common single-segment case
- Keep all other decoder behavior (CONTINUATION tracking, maxFrameSize validation, unknown-frame skipping, `_frames` list reuse, Reset/Dispose) unchanged
- Update all decoder call sites mechanically so the codebase compiles and existing behavior is preserved for the common (single-segment, full-frame) case
- Add multi-segment test coverage for the new parsing path

**Non-Goals:**
- No transport changes — no `Pipe`, no `IConnectionTransport`, no read/write pumps (Phase A)
- No change to how/when SMs invoke `DecodeAll`, beyond wrapping the existing `WireBuffer.Memory` in a `ReadOnlySequence<byte>`
- No encoder changes
- No stage logic changes
- Not attempting to fully preserve cross-call partial-frame continuity without a real Pipe (see Risk below) — that correctness property is restored by Phase A/C, not by Phase B

## Decisions

### Decision 1: `ReadOnlySequence<byte>` input, `SequencePosition` output

```
IReadOnlyList<TFrame> DecodeAll(in ReadOnlySequence<byte> input, out SequencePosition consumed)
```

`consumed` is the position up to which the decoder fully decoded frames. It mirrors exactly what the master plan's SM will later pass to `PipeReader.AdvanceTo(consumed, examined)`. Passing `input` by `in` avoids a defensive copy of the (relatively large, 3-`readonly`-field) `ReadOnlySequence<byte>` struct on every call.

### Decision 2: `SequenceReader<byte>` for parsing, `FirstSpan` fast path

Frame header parsing (9 fixed bytes for H2; QUIC varint type+length for H3) uses `SequenceReader<byte>.TryRead`/`TryReadBigEndian` helpers, which transparently handle both single- and multi-segment sequences. Before falling into the generic reader path, both decoders check `input.IsSingleSegment` (or slice `input.FirstSpan`) and run the existing span-based fast loop unchanged — this is the overwhelmingly common case (one WireBuffer per read, no real Pipe segmentation yet in Phase B) and keeps decode cost identical to today's.

```
if (input.IsSingleSegment)
{
    // existing span-walking loop, operating on input.FirstSpan
}
else
{
    // SequenceReader<byte>-based loop for cross-segment frames
}
```

### Decision 3: No internal remainder storage — decoder is a pure function of its input plus persistent protocol state

`_remainderBuffer`/`_remainderOffset`/`_remainderLength` (H2) and the analogous H3 fields are deleted. The decoder still carries protocol state that legitimately spans calls (`_awaitingContinuationStreamId` for H2 CONTINUATION tracking, H3's varint-in-progress tracking) — only the *byte-buffering* remainder mechanism is removed, not all cross-call state.

When `input` ends mid-frame, the decoder simply stops: `consumed` points to the position before the incomplete frame, and no bytes are copied anywhere. This matches the master plan's contract (`pipe-transport-tcp/specs/frame-decoder-contract/spec.md`, "Remainder handling via Pipe AdvanceTo") exactly, so Phase B and Phase A specs compose without rework when they are integrated.

### Decision 4: SM call sites wrap `WireBuffer.Memory`, disposal unchanged

```csharp
// Transitional call-site shape for the remainder of Phase B (until Phase A lands):
var seq = new ReadOnlySequence<byte>(data.Buffer.Memory);
var frames = decoder.DecodeAll(in seq, out SequencePosition consumed);
// ... consume frames ...
data.Buffer.Dispose(); // unchanged — SM still owns and disposes WireBuffer as today
```

This is a single-segment `ReadOnlySequence<byte>` by construction, so it always takes the fast path from Decision 2. `consumed` is not currently plumbed anywhere new by the SM — Phase B does not add an `AdvanceTo` call because there is no Pipe to advance. The SM's existing "did we consume everything" bookkeeping (if any) that previously relied on `bytesConsumed == input.Length` is preserved because, in the single-segment case, `consumed` reaching `seq.End` is the equivalent condition — this is a mechanical value swap only.

## Risks / Trade-offs

**[Cross-call partial-frame continuity gap during Phase B]** Before Phase A lands, if a single `WireBuffer` read ends mid-frame, the trailing partial bytes are no longer buffered by anyone: the decoder no longer copies them (Decision 3), and the SM does not gain a replacement buffer in Phase B (out of scope). This is a real, accepted regression relative to today's behavior for the (rare, but not impossible) case of a frame split across two socket reads.
  - **Mitigation 1 (scope control):** Phase B's test suite validates the decoder's API contract directly via constructed `ReadOnlySequence<byte>` fixtures (single- and multi-segment), not through the live SM/network path — so the gap does not surface in CI.
  - **Mitigation 2 (gating):** the SM call-site change lands but is not exercised against real fragmented network reads in production until Phase A's Pipe-backed transport (which restores continuity via `AdvanceTo(consumed, examined)`) is merged. Track this explicitly as a pre-release gate in `tasks.md` for both Phase B and the master plan — Phase B must not ship to a release branch alone if it is reachable by real fragmented TCP reads.
  - **Mitigation 3 (fallback, if gating proves impractical):** if Phase B needs to be safely deployable standalone, add a minimal transitional accumulator at the SM call site (not in the decoder) that concatenates a held-over tail with the new `WireBuffer.Memory` before wrapping in `ReadOnlySequence<byte>`. This is deliberately deferred — it would duplicate work that Phase A's Pipe does for free — and should only be added if Phase B ships to production ahead of Phase A.

**[Two parsing code paths per decoder]** The single-segment fast path (Decision 2) and the `SequenceReader<byte>` multi-segment path are separate code, doubling the surface that must stay behaviorally identical (same frame validation, same exception messages/RFC references, same CONTINUATION/maxFrameSize checks). Mitigation: shared `CreateFrame`/validation helpers are reused by both paths; only the header-field extraction and offset-walking differ. Multi-segment tests assert identical decoded output to the single-segment case for the same logical byte stream, just split across segment boundaries.

**[Breaking signature change]** All ~70 files that call `DecodeAll` (production SM code + tests) must update in the same change to keep the build green. Mitigation: mechanical, low-risk edit (`ReadOnlyMemory<byte>` → `new ReadOnlySequence<byte>(memory)`, `out int` → `out SequencePosition`); suitable for a bulk mechanical pass per the project's own convention (group by protocol, e.g., H2 test files then H3 test files).
