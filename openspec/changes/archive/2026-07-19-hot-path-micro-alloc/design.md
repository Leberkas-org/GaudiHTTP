## Context

Four independent micro-optimizations identified from the Artery comparison (2026-07-19). All are mechanical, low-risk, and address known allocation hotspots. No architectural changes.

## Goals / Non-Goals

**Goals:**
- Eliminate unnecessary heap allocations on hot paths (encoder/decoder, timer, body pump, server body)
- All changes are purely internal — no public API impact
- Each item independently testable and revertable

**Non-Goals:**
- H3 FrameDecoder reuse (separate change: h3-zero-alloc)
- Outbound gather-write (#6 from the analysis — needs benchmark evidence)
- Architectural changes to body pump or transport
- Benchmark runs (separate session on benchmark hardware)

## Decisions

### D1: stackalloc scope — synchronous actor-thread paths only

stackalloc ONLY where the buffer does not live across an await. All encoders/decoders run synchronously within the actor turn — always safe. Pattern:

```csharp
// Before: implicit rent or inline write with offset arithmetic
// After:
Span<byte> header = stackalloc byte[9];  // H2 Frame Header
header[0] = (byte)(length >> 16);
// ...
outputBuffer.Write(header);
```

Maximum stackalloc size: 64 bytes (H2 frame header = 9, QUIC varint = 8, HPACK integer = 10). No large stackallocs.

### D2: Timer keys — struct key instead of string

Current: `string.Concat("body-consumption:", streamId.ToString())` (3 strings per stream).
New: `readonly record struct TimerKey(TimerKind Kind, int StreamId)` as dictionary key.

`TimerKind` as byte-sized enum: `BodyConsumption`, `HeadersTimeout`, `IdleTimeout` etc.
Struct as key = zero-alloc (stack-allocated, value equality). `Dictionary<TimerKey, ...>` works with default struct equality (all fields are primitive).

Affects: `StreamState.SetTimerKeys()` (H2 + H3), `HttpClientConnectionStageLogic` timer scheduling, `HttpServerConnectionStageLogic` timer scheduling. Akka `TimerGraphStageLogic` uses `object` as timer key — struct gets boxed. Alternative: `int`-packed key (`(kind << 24) | streamId`) — no boxing. StreamId range: H2 = int (31 bit), H3 = long (62 bit). For H3 a `long`-packed key is needed.

### D3: SerialBodyPump — EnsureBuffer with ownership-transfer exception

FlowControlledBodyPump pattern: `PumpSlot.EnsureBuffer(chunkSize)` — rent once, reuse across reads. SerialBodyPump gets the same. **Exception**: H1.1 identity encoding uses `WireBuffer.Wrap(owner)` for zero-copy transfer — the buffer MUST be given away (ownership transfer to transport). Solution:

- Chunked/Content-Length: Buffer reuse (rent once, emit Span slice, keep ownership)
- Identity: Fresh rent + Wrap (ownership goes to transport, pump rents next)

Detection: `SerialBodyPump` knows which framing mode is active (configured at stream start).

### D4: ValueTask server body-read — API compatibility check

`GaudiHttpResponseBodyFeature` implements an internal interface. Return type change from `Task<T>` to `ValueTask<T>` is only possible if the interface allows it. If not: adapt interface (internal, no public API break).

Synchronous fast path: `new ValueTask<ReadResult>(result)` — zero-alloc.
Async slow path: Framework allocates IValueTaskSource internally — identical to existing Task.FromResult.

## Risks / Trade-offs

- **[stackalloc + large methods]** → No risk: Encoder methods are not recursive, stack frame is small. 64 bytes stackalloc is trivial.
- **[Timer key boxing]** → Mitigation: int/long-packed key instead of struct, no boxing with `object`-keyed Akka timer API.
- **[SerialBodyPump identity mode]** → Mitigation: Explicit distinction between chunked and identity in the pump. Unit test verifies that identity mode still uses WireBuffer.Wrap (zero-copy E2E).
- **[ValueTask double-await]** → No risk: Server body read is consumed exactly once. ValueTask contract (await once) is satisfied.
