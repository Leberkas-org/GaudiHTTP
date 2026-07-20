## Why

H3 FrameDecoder and QpackInstructionDecoder allocate 12x `MemoryPool<byte>.Shared.Rent` per frame/instruction (remainder buffering, header block copies, Settings/GOAWAY copies). The H2 FrameDecoder has already solved the identical problem: `_workingBuffer` adoption + reusable `_frames` list. H3 does rent-and-dispose on the same actor thread — identical preconditions, missing pattern port. After the pump-credit fix (feat/outbound-flow-control), these rent sites are the largest remaining H3 alloc driver.

## What Changes

- H3 `FrameDecoder`: Working buffer adoption instead of per-frame `MemoryPool.Shared.Rent`. Remainder buffer as field instead of per-frame rent. Dispose previous buffer on next `Decode()` call (like H2).
- H3 `QpackInstructionDecoder`: Same pattern — replace 6 rent sites with buffer reuse.
- Reusable `_frames` list in H3 FrameDecoder (Clear+Repopulate, as H2 already does).
- New unit tests verifying buffer reuse and no leaks (analogous to `Http2DecoderReuseSpec`).

## Capabilities

### New Capabilities
- `h3-decoder-buffer-reuse`: Port of the H2 working-buffer-adoption pattern to H3 FrameDecoder and QpackInstructionDecoder — eliminates per-frame MemoryPool rents through field-based buffer reuse under actor confinement.

### Modified Capabilities

## Impact

- `src/GaudiHTTP/Protocol/Syntax/Http3/FrameDecoder.cs` — 6 MemoryPool.Shared.Rent sites → buffer adoption
- `src/GaudiHTTP/Protocol/Syntax/Http3/QpackInstructionDecoder.cs` — 6 MemoryPool.Shared.Rent sites → buffer reuse
- `src/GaudiHTTP.Tests/Protocol/Syntax/Http3/` — new reuse specs
- Reference pattern: `src/GaudiHTTP/Protocol/Syntax/Http2/FrameDecoder.cs`
- Expected impact: H3 alloc −30–50% on decoder paths
- Risk: Medium — decoder semantics must be preserved exactly (frame boundaries, remainder handling)
