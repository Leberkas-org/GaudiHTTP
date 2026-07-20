## Context

H3 `FrameDecoder` and `QpackInstructionDecoder` use `MemoryPool<byte>.Shared.Rent` at 12 sites. Code analysis reveals a different picture than initially assumed:

**Production callers** (Client `StreamManager.cs:49`, Server `Http3ServerSessionManager.cs:484`) exclusively use `DecodeAll(ReadOnlyMemory<byte>)` with `sliceInput: true`. The caller retains WireBuffer ownership; frames get zero-copy Memory slices.

**Of the 6 FrameDecoder Rent sites:**
- Sites 1–3 (L55, L81, L103): Remainder management — actual hot path, fire on incomplete frames
- Sites 4–6 (L267, L285, L348): Payload copies — only in `!sliceInput` path, DEAD CODE in production

**Consequence:** No WireBuffer adoption pattern like H2 (decoder receives Memory, not WireBuffer). Instead: field-based remainder + dead code cleanup. Rebuilding H2's decoder to the same pattern = separate follow-up change (`unified-decoder-pattern`).

## Goals / Non-Goals

**Goals:**
- Eliminate all 6 `MemoryPool<byte>.Shared.Rent` sites in H3 FrameDecoder (3 remainder + 3 dead payload copies)
- Eliminate all 6 sites in QpackInstructionDecoder
- Replace remainder rents with field-based `byte[]` (grow-on-demand, never-shrink)
- Remove `ReadOnlySpan<byte>` overloads (no production caller, they enable the dead payload-rent paths)
- Verify `_frames` list reuse (already a field with Clear+Repopulate) and add test coverage
- Unit tests for remainder reuse and cleanup
- No behavioral change in frame parsing

**Non-Goals:**
- Modify H2 FrameDecoder (separate change: `unified-decoder-pattern`)
- WireBuffer adoption in H3 decoder (not applicable — caller owns WireBuffer)
- stackalloc optimizations (separate change: `hot-path-micro-alloc`)
- Benchmark runs (separate session on benchmark hardware)

## Decisions

### D1: Field-based remainder buffer (byte[], not IMemoryOwner)

`_remainderOwner` (IMemoryOwner<byte>) replaced by:
```csharp
private byte[]? _remainderBuffer;  // grow-on-demand, never shrink
private int _remainderLength;
```

Why `byte[]` instead of `IMemoryOwner`/`WireBuffer`:
- Remainder is same-thread (actor confinement) — no cross-thread pool needed
- Remainder is long-lived (survives across Decode() calls) — no pool churn
- `byte[]` is GC-managed, no Dispose needed, no leak risk
- Grow-on-demand: allocate larger array when needed, never shrink
- Analogous to `_huffmanScratch` / `_hpackScratch` fields in the compression decoders

### D2: Remove Span-only overloads and payload-rent paths

`TryDecode(ReadOnlySpan<byte>, ...)` and `DecodeAll(ReadOnlySpan<byte>, ...)` are removed:
- No production caller
- They enable the `sliceInput: false` path which forces the 3 payload rents (Sites 4–6)
- Without these overloads, `sliceInput` is always `true` → `DecodeDataFrame`, `DecodeHeadersFrame`, `DecodePushPromiseFrame` always return zero-copy slices
- The `sliceInput` parameter and associated branches become obsolete

The combine logic (Site 1, L55) remains but copies into the field-based `_remainderBuffer` instead of renting.

### D3: `_frames` list — already correct, just add tests

The `_frames` list is already a field with `[]` initializer and `Clear()` at the start of `DecodeAllCore`. No code change needed — just test coverage for same-instance reuse.

### D4: QpackInstructionDecoder — same remainder pattern

6 rent sites follow the same pattern: remainder buffer for incomplete instructions. Same field-based `byte[]` solution. QpackInstructionDecoder is per-connection (not per-stream, not pooled) — simpler lifecycle.

### D5: OnReset()/Dispose() cleanup

FrameDecoder is pooled per stream. `OnReset()` must:
- Set `_remainderBuffer` to null (GC-collected, no Dispose needed)
- Set `_remainderLength` to 0
- Clear `_frames`

Dispose path (stream error without pool return): same cleanup.
Since `byte[]` is GC-managed, there's no leak risk from missed cleanup — just memory hygiene.

## Risks / Trade-offs

- **[byte[] vs pool]** → `byte[]` lands on LOH if > 85KB. H3 remainders are typically < 16KB (one QUIC datagram). No LOH risk.
- **[Span overload removal is breaking]** → Internal API only, no public API break. Tests using the Span overload will be updated to Memory overloads.
- **[byte[] never shrinks]** → Worst case: a stream with a large frame leaves a large remainder array in the pooled decoder. `OnReset()` sets to null → next rent starts fresh.
- **[Reusable list + async consumption]** → No risk: H3 frame processing is synchronous within the actor turn (verified).

## Follow-up Changes

- **`unified-decoder-pattern`**: Rebuild H2 FrameDecoder to H3's caller-ownership pattern (Memory slicing, remainder-only decoder). Unifies the decoder contract across all protocols.
