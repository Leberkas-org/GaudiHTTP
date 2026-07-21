## Why

`FrameDecoder` (H2, H3) currently accepts `ReadOnlyMemory<byte>` and owns a field-level `_remainderBuffer`/`_remainder` byte[] to buffer partial frames across `DecodeAll` calls. The master `pipe-transport-tcp` plan replaces the TCP transport with `System.IO.Pipelines`, whose `PipeReader.AdvanceTo(consumed, examined)` retains unconsumed bytes natively — making the decoder's own remainder buffer redundant duplication once the Pipe is in place. Phase B pulls the decoder-facing half of that change forward and lands it independently of the transport rewrite (Phase A), so the two efforts can proceed in parallel without a shared branch dependency.

## What Changes

- **GaudiHTTP FrameDecoders**: `DecodeAll` signature changes from `(ReadOnlyMemory<byte> input, out int bytesConsumed)` to `(in ReadOnlySequence<byte> input, out SequencePosition consumed)` for both H2 and H3 decoders. **BREAKING** for all direct callers of `FrameDecoder.DecodeAll`.
- **GaudiHTTP FrameDecoders**: Internal `_remainderBuffer`/`_remainderOffset`/`_remainderLength` (H2) and equivalent H3 fields are removed. Parsing is rewritten against `SequenceReader<byte>` with a single-segment fast path (`sequence.FirstSpan`) for the common case.
- **GaudiHTTP Protocol State Machines**: Call sites are updated mechanically to wrap the existing `WireBuffer.Memory` in `new ReadOnlySequence<byte>(memory)` before calling `DecodeAll`. No other SM logic changes — the SM still disposes `WireBuffer` exactly as before.
- **Transitional limitation (accepted for Phase B)**: without a real `Pipe` underneath, unconsumed bytes are no longer retained across `DecodeAll` calls by anyone (the decoder no longer buffers them, and the SM does not yet own a replacement buffer). This is intentional — Phase B lands the API shape and internal parsing rewrite; full cross-call correctness for partial frames returns once Phase A's `IConnectionTransport`/Pipe wiring is integrated (see `pipe-transport-tcp` design.md Decision 6). See design.md for the accepted-risk write-up and gating plan.
- **Tests**: All existing `FrameDecoder` unit/stage tests updated to the new signature (wrap fixtures in `new ReadOnlySequence<byte>(memory)`). New tests added for multi-segment `ReadOnlySequence` inputs (header spanning segments, payload spanning segments).

## Capabilities

### Modified Capabilities
- `frame-decoder-contract`: Input changes from `ReadOnlyMemory<byte>` to `ReadOnlySequence<byte>`. Output changes from `int bytesConsumed` to `SequencePosition consumed`. Internal remainder buffering removed. Zero-copy guarantee extended/qualified for multi-segment sequences.

## Impact

- **GaudiHTTP** (`src/GaudiHTTP/Protocol/Syntax/Http2/FrameDecoder.cs`, `src/GaudiHTTP/Protocol/Syntax/Http3/FrameDecoder.cs`): signature and internal parsing rewrite.
- **GaudiHTTP SM call sites**: `Http2ClientSessionManager`, `Http2ServerSessionManager`, `Http3` `StreamManager`/`Http3ServerSessionManager` (and any other direct `DecodeAll` caller) — mechanical wrap of `WireBuffer.Memory` in `ReadOnlySequence<byte>`, unwrap `SequencePosition` back to a byte count only where still needed for existing bookkeeping.
- **Tests** (`GaudiHTTP.Tests/Protocol/Syntax/Http2/Frames/*`, `GaudiHTTP.Tests/Protocol/Syntax/Http3/Frames/*`, and the ~70 files listed by `DecodeAll` usage across Client/Server/Security/Stages folders): signature updates plus new multi-segment coverage.
- **No changes**: transport (Phase A, `pipe-transport-tcp`), encoders, stage logic, H2/H3 flow control, CONTINUATION state machine, maxFrameSize validation — all preserved as-is, only re-plumbed onto the new input type.
- **Sequencing**: Independent of Phase A; can be developed, reviewed, and merged in parallel. Production activation of the transitional remainder gap should be reconciled with Phase A before it reaches a release branch (see design.md Risk).
