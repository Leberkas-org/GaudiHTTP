## Why

H2 and H3 FrameDecoders use fundamentally different ownership patterns today. H3's pattern (caller owns WireBuffer, decoder gets Memory slices, frames reference caller's memory) is strictly better: the buffer dies immediately after frame processing instead of living until the next `Decode()` call. H2's decoder adopted WireBuffer ownership as a historical design choice, not because it's necessary — H2 state machines also consume frames synchronously within the same actor turn.

Unifying on H3's pattern simplifies the H2 FrameDecoder (no `_workingBuffer`, no adoption, no offset-wrapped-buffer fallback), reduces peak memory (no extra buffer alive between decode calls), and creates a single decoder contract across all protocols.

Depends on: `h3-zero-alloc` (which establishes the field-based remainder pattern that H2 will also adopt).

## What Changes

- H2 `FrameDecoder.Decode(WireBuffer)` → `DecodeAll(ReadOnlyMemory<byte>, out int)` (caller-ownership pattern, matching H3)
- Remove `_workingBuffer` field and WireBuffer adoption logic
- Add field-based `byte[]` remainder (same pattern as H3 post-`h3-zero-alloc`)
- H2 state machines updated: keep WireBuffer ownership, pass `buffer.Memory`, dispose after frame processing
- Frames reference caller's memory via slicing (zero-copy, same as today but with caller-controlled lifetime)

## Capabilities

### New Capabilities
- `unified-decoder-ownership`: Caller-owned buffer pattern for H2 FrameDecoder, matching H3's existing pattern — eliminates WireBuffer adoption, offset-wrapped fallback, and inter-decode buffer retention.

### Modified Capabilities

## Impact

- `src/GaudiHTTP/Protocol/Syntax/Http2/FrameDecoder.cs` — remove _workingBuffer, add byte[] remainder, change signature
- `src/GaudiHTTP/Protocol/Syntax/Http2/Client/Http2ClientSessionManager.cs` — keep WireBuffer ownership, pass Memory
- `src/GaudiHTTP/Protocol/Syntax/Http2/Server/Http2ServerSessionManager.cs` — same
- `src/GaudiHTTP.Tests/Protocol/Syntax/Http2/` — update all FrameDecoder tests to new signature
- Risk: Medium — H2 FrameDecoder has the most extensive test coverage in the project; regression risk managed by existing tests
