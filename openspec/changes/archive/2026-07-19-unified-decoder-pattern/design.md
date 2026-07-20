## Context

After `h3-zero-alloc`, H3 FrameDecoder uses caller-owned buffers: callers pass `ReadOnlyMemory<byte>`, frames get zero-copy slices, remainder is a field-based `byte[]`. H2 FrameDecoder still takes `WireBuffer` ownership: adopts it as `_workingBuffer`, frames reference adopted memory, buffer lives until next `Decode()`.

Both callers (Client `Http2ClientSessionManager.DecodeFrames:354` and Server `Http2ServerSessionManager.DecodeClientData:151`) consume frames synchronously — the buffer doesn't need to outlive the frame loop.

## Goals / Non-Goals

**Goals:**
- Change H2 `FrameDecoder.Decode(WireBuffer)` to `DecodeAll(ReadOnlyMemory<byte>, out int)`
- Remove `_workingBuffer` field and WireBuffer adoption logic
- Add field-based `byte[]` remainder (same pattern as H3 post-`h3-zero-alloc`)
- Callers keep WireBuffer ownership, pass `buffer.Memory`, dispose after frame processing
- Frames reference caller's memory (zero-copy, caller-controlled lifetime)
- Preserve all existing FrameDecoder test behavior

**Non-Goals:**
- Change frame types or frame parsing logic
- Modify any protocol behavior
- Touch H3 FrameDecoder (already done)

## Decisions

### D1: Same two-path design as H3

```
DecodeFromInput(input)        — no remainder, zero-copy from caller's memory
DecodeWithRemainder(input)    — merge into byte[] field, deferred compaction
```

Identical to H3 FrameDecoder's design. Remainder offset tracked via `_remainderOffset`/`_remainderLength`, compacted at the start of next `DecodeAll` call (never while frames reference the buffer).

### D2: Callers take WireBuffer ownership

```csharp
// Before (decoder owns):
return _frameDecoder.Decode(buffer);

// After (caller owns):
using var inputBuffer = buffer;
return _frameDecoder.DecodeAll(inputBuffer.Memory, out _);
```

Server already has a `try/catch` around the decode loop — `using var` naturally disposes on both success and exception paths. Client's `DecodeFrames` method becomes the owner.

### D3: `maxFrameSize` constructor parameter preserved

The existing `maxFrameSize` validation in the decode loop is preserved exactly. The only change is how bytes arrive (Memory vs WireBuffer), not how frames are validated.

### D4: `Reset()` / `Dispose()` simplified

Current `Reset()` and `Dispose()` dispose `_workingBuffer`. New version sets `_remainderBuffer = null` — simpler, no pool-return needed (GC-managed `byte[]`).

## Risks / Trade-offs

- **[Large test surface]** → H2 FrameDecoder has the most tests in the project (~60 tests across 4 spec files). Signature change requires updating all callers. Risk mitigated by: test count validates correctness after migration.
- **[Frame payload lifetime]** → Payloads now reference caller's memory. If a caller were to dispose the WireBuffer before consuming frames, payloads would be corrupted. Both production callers consume synchronously — no risk. Test callers that retain frames must be updated.
- **[Remainder buffer on LOH]** → Same as H3: H2 frames are bounded by SETTINGS_MAX_FRAME_SIZE (default 16 KB). Remainder never exceeds this. No LOH risk.
