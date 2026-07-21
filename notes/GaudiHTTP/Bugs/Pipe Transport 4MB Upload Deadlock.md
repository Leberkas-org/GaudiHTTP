---
tags:
  - bug
  - pipe-transport
  - deadlock
  - backpressure
date: 2026-07-21
severity: blocking
phase: Phase D
---
# Pipe Transport 4MB Upload Deadlock

**Status**: Open — root cause understood, fix requires native Pipe redesign
**Branch**: `feat/pipe-transport-tcp`
**Reproducer**: `GaudiHTTP.IntegrationTests.End2End.H11.LargePayloadSpec.LargePayload_should_roundtrip_upload_exceeding_the_connection_credit_budget`

## Symptom

4 MiB POST echo-roundtrip times out after 25s. Smaller payloads (128 KiB) work fine.

## The Deadlock Chain

Four components form a circular block:

```
┌─────────────────────────────────────────────────────────────────┐
│  CLIENT                                                         │
│                                                                 │
│  SerialBodyPump ──writes──▶ Output Pipe ──read──▶ WritePump     │
│       ▲ credit                  │ FlushAsync         │ SendAsync│
│       │                         │ blocks             │ blocks   │
│       │                         ▼                    ▼          │
│  OnFlushCompleted ◀── pipe resume threshold    TCP send buffer  │
│       never fires              never reached        full        │
│                                                      │          │
├──────────────────────────────────────────────────────┼──────────┤
│  TCP                                                 │          │
│                                          TCP window  │          │
│                                          closes ◀────┘          │
│                                             │                   │
├─────────────────────────────────────────────┼───────────────────┤
│  SERVER                                     ▼                   │
│                                                                 │
│  ReadPump ──writes──▶ Input Pipe ──read──▶ StateMachine         │
│     │ FlushAsync         │                    │ TryEnqueue      │
│     │ blocks             │                    ▼                 │
│     ▼                    ▼               QueuedBodyReader       │
│  stops reading     pause threshold       IsFull = true          │
│  from socket       reached                    │                 │
│                                               ▼                 │
│                                    ShouldPauseReads = true      │
│                                    → RequestRead() skipped      │
│                                    → pipe not consumed          │
│                                    → ReadPump stays blocked     │
└─────────────────────────────────────────────────────────────────┘
```

## Root Cause: AdvanceTo Granularity in the WritePump

`TransportPumps.RunWritePump` (servus.akka) reads the **entire** pipe buffer via `ReadAsync`, iterates over all segments with `SendAsync`, and only calls `AdvanceTo(buffer.End)` **after all sends complete**:

```csharp
// TransportPumps.cs:48-69
var result = await reader.ReadAsync(ct);
foreach (var segment in result.Buffer)
{
    await socket.SendAsync(segment, ...);  // ← blocks on TCP backpressure
}
reader.AdvanceTo(result.Buffer.End);       // ← pipe sees "consumed" only HERE
```

**Why this deadlocks**: When `SendAsync` blocks (TCP window closed), all read bytes remain as "read but unconsumed" in the pipe. The pipe counts these against `pauseWriterThreshold` (512 KB). The writer side (protocol layer) cannot flush → body pump gets no credit → no new body read → no progress.

**Why partial AdvanceTo doesn't work**: `PipeReader` allows exactly **one** `AdvanceTo` per `ReadAsync`. You can partially consume (only the first segment), but then you must call `ReadAsync` again for the rest. This alone isn't sufficient because:

1. A single segment can already exceed 256 KB (the pipe resume threshold)
2. Even with partial consume: if TCP blocks on the first segment, the problem persists
3. The pipe thresholds (512 KB pause / 256 KB resume) are structurally too small for a 4 MB payload

## Why the Server Also Stalls

Even if the client sends correctly, the server has the same problem in mirror:

1. Server `ReadPump` reads socket → writes to input pipe
2. Server `DecodeData` reads from pipe → `TryEnqueue` on `QueuedBodyReader`
3. `QueuedBodyReader.IsFull` (capacity = `_backpressureThreshold`) → `ShouldPauseReads = true`
4. `TransportIo.RequestRead()` is skipped (`_shouldPause()` returns true)
5. Input pipe is not consumed further → ReadPump `FlushAsync` blocks → socket read stops
6. TCP receive window fills → client `SendAsync` blocks

The handler (`CopyToAsync` into MemoryStream) is fast enough, but the `SlotFreed` → `BodyResumed` → `RequestRead()` cycle must traverse the actor mailbox and competes with the same thread that runs `DecodeData`.

## Structural Problem

The design has an **impedance mismatch**: the pipe is a byte-stream with backpressure thresholds, but the WritePump treats it as a message buffer (read all, send all, acknowledge all). The core problem:

| Aspect | Current | Target (pipe-native) |
|--------|---------|---------------------|
| WritePump consume | Batch (all after all sends) | Incremental (after each send) |
| Pipe thresholds | Static 512/256 KB | Irrelevant when consume is granular |
| Body credit | Flush-based (park/resume) | Consume-based (AdvanceTo as signal) |
| Server body read | Actor-pumped (SlotFreed→mailbox→RequestRead) | Direct pipe-to-stream (no actor hop) |

## Affected Components

| File | Problem |
|------|---------|
| `servus.akka/.../TransportPumps.cs` | WritePump: batch AdvanceTo |
| `GaudiHTTP/Protocol/TransportIo.cs` | Flush-based credit system (park/resume) |
| `GaudiHTTP/Protocol/Body/SerialBodyPump.cs` | Credit tied to flush, not actual consume |
| `GaudiHTTP/Protocol/Body/QueuedBodyReader.cs` | IsFull blocks entire pipe-read chain |
| `GaudiHTTP/Protocol/TcpStateMachineBase.cs` | ShouldPauseReads = IsFull couples body backpressure to transport |
| `servus.akka/.../PipeTransportOptions.cs` | Static thresholds (512/256 KB) |

## Solution Direction: Native Pipe Design

Rather than patching the existing architecture (inflating thresholds, partial consume), **rewrite pipe-native from scratch**:

1. **WritePump**: Segment-by-segment consume — after each successful `SendAsync`, immediately `AdvanceTo` for exactly that segment, then next `ReadAsync`
2. **Credit system**: Based on actual socket consume (AdvanceTo bytes), not pipe flush events
3. **Server body**: Direct pipe pass-through instead of actor-pumped QueuedBodyReader where possible
4. **Thresholds**: Become non-critical when consume is granular — can stay small for memory efficiency
