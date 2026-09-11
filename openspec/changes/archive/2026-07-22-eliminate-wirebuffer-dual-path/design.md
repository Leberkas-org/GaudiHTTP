## Context

The pipe-transport migration (phases A–E) established `IConnectionTransport`, `TransportIo`, and `TcpStateMachineBase` as the production I/O path for TCP protocols. However, every state machine retained a dual-path `if (Transport is { }) ... else ... WireBuffer.Rent + Ops.OnOutbound` pattern. The `else` branch was intended as a pre-connect fallback, but in practice `Transport` is always non-null when SMs encode or decode data: `TransportConnected` arrives before any data processing, and `TransportDisconnected` sets `ShouldComplete` which prevents further encoding.

The current flow on the write-side has a wasteful double copy:

```
Encoder → WireBuffer.Rent → encode into WireBuffer
       → EmitWireBuffer → transport.GetMemory → copy → Advance → Flush
```

Six copies of `EmitWireBuffer` exist across H1.0/H1.1/H2 SMs (client+server), all identical. The H2 SessionManagers have their own `EmitWireBuffer` that delegates through an `EmitBuffer: Action<WireBuffer>` delegate back to the SM's version.

## Goals / Non-Goals

**Goals:**
- Eliminate the `WireBuffer.Rent → encode → copy into pipe` double-copy on the TCP write path
- Remove all `if (Transport) / else WireBuffer` dual-path branches from TCP SMs
- Remove the `DecodeServerData`/`DecodeClientData` `TransportData` fallback for TCP
- Establish `InMemoryTransport` as the standard test-infrastructure for SM unit tests
- Reduce ~6 copies of `EmitWireBuffer` to zero

**Non-Goals:**
- Changing H3/QUIC I/O (stays on `WireBuffer`/`MultiplexedData`)
- Removing `WireBuffer` from servus.akka (still needed for QUIC)
- Changing the Akka Streams port shape (lifecycle events stay on ports)
- Migrating all ~100+ tests in one pass (phased is fine)

## Decisions

### Decision 1: H1 Encoders write directly into TransportBufferWriter

**Chosen**: H1 encoders (`Http11ServerEncoder.Encode`, `Http11ClientEncoder.WriteTo`, H1.0 equivalents) already accept `IBufferWriter<byte>`. Today the SM creates a `WireBufferWriter(wireBuffer)` and passes it. Instead, the SM passes `new TransportBufferWriter(Transport)` directly. No `WireBuffer.Rent`, no intermediate copy.

**Alternative considered**: Introduce an `ArrayBufferWriter<byte>` as the intermediate buffer. Rejected — adds an unnecessary allocation when `TransportBufferWriter` already implements `IBufferWriter<byte>` and writes zero-copy into the pipe.

**Edge case — coalesced body in H1.1 server**: Response headers + buffered body are currently written into a single `WireBuffer` to avoid two socket writes. With `TransportBufferWriter`, headers and body are written sequentially into the same pipe buffer, achieving the same coalescing without an explicit rent. The pipe's internal buffering handles this — `FlushAsync` is called once after both writes.

### Decision 2: H2 SessionManager serializes frames into a scratch buffer, SM copies to pipe

**Chosen**: Replace `EmitBuffer: Action<WireBuffer>` with `EmitData: Action<ReadOnlySpan<byte>>`. The SessionManager serializes frames into a reusable `byte[]` scratch buffer (already pattern-established by `_hpackScratch` in `Http2ClientEncoder`), then calls `EmitData(scratch.AsSpan(0, written))`. The SM's `EmitData` callback does `transport.GetMemory(len)` + copy + `Advance` + `RequestFlush()`.

**Why not give the SessionManager the Transport directly?** The SessionManager is already deeply coupled to `IClientStageOperations`/`IServerStageOperations`. Adding `IConnectionTransport` would create a second output channel and require the SessionManager to own flush coordination. Keeping the SM as the single pipe-writer preserves the invariant that only `TcpStateMachineBase` interacts with `IConnectionTransport`.

**Why a scratch buffer instead of TransportBufferWriter?** H2 frame serialization uses `WriteTo(ref Span<byte>)` which advances a span by-ref — it doesn't use `IBufferWriter<byte>`. Converting all frame types to `IBufferWriter<byte>` would be a large API change for no benefit. A per-SessionManager scratch buffer (grown on demand, cleared per use) is zero-alloc on the hot path and matches the existing `_hpackScratch` pattern.

**Sizing**: The scratch buffer starts at 64KB (covers most frame batches). For large DATA frame batches the SM can write in chunks, or the scratch buffer grows once and stays at the high-water mark for the connection's lifetime. No per-write allocation.

### Decision 3: InMemoryTransport replaces FakeOps.Outbound for wire assertions

**Chosen**: A new `InMemoryTransport : IConnectionTransport` in `GaudiHTTP.Tests.Shared` that:
- **Read side**: `Feed(byte[] data)` enqueues a `ReadResult`. `Feed(ReadOnlySequence<byte>)` for multi-segment testing. `Complete()` enqueues a completed read.
- **Write side**: `GetMemory` returns from an internal `ArrayBufferWriter<byte>`. `Advance` tracks written bytes. `WrittenBytes` property returns everything written. `TakeWrittenBytes()` returns and clears (for sequential assertions).
- **Flush**: Configurable sync/async/completed modes (like `ScriptableTransport.FlushMode`).
- **Lifecycle**: `Info` returns `ConnectionInfo.None` by default.

Tests migrate from:
```csharp
// Old: create WireBuffer, feed via DecodeClientData, assert on ops.Outbound
var buf = WireBuffer.Rent(data.Length);
data.CopyTo(buf.FullMemory.Span);
buf.Length = data.Length;
sm.DecodeClientData(TransportData.Rent(buf));
Assert.IsType<TransportData>(ops.Outbound[0]);
var text = Encoding.ASCII.GetString(((TransportData)ops.Outbound[0]).Buffer.Span);
```
to:
```csharp
// New: feed via transport, assert on transport.WrittenBytes
var transport = new InMemoryTransport();
sm.DispatchLifecycleEvent(new TransportConnected(ConnectionInfo.None, transport));
transport.Feed(data);
sm.TryHandleAsyncResult(new ReadCompleted(transport.DequeueReadResult(), gen));
var text = Encoding.ASCII.GetString(transport.WrittenBytes);
```

### Decision 4: Remove DecodeServerData/DecodeClientData TransportData fallback

The `if (Transport is null && data is TransportData { Buffer: var buffer })` branch in all TCP SMs is dead code in production. It existed for:
1. Pre-connect buffered data — doesn't happen (Transport is set before data flows)
2. Tests that don't connect a transport — these migrate to `InMemoryTransport`

After migration, `DecodeServerData`/`DecodeClientData` for TCP protocols only dispatches lifecycle events via `DispatchLifecycleEvent(data)`. If the event is not a lifecycle event and Transport is null, it's a programming error (assert/throw).

### Decision 5: Phased execution order

1. **InMemoryTransport** — build the test infrastructure first, unblocking everything else
2. **H1 write-side** — simplest encoders, highest confidence, proves the pattern
3. **H1 read-side** — remove TransportData fallback, migrate affected tests
4. **H2 write-side** — EmitBuffer→EmitData, scratch buffer, migrate SessionManager tests
5. **H2 read-side** — remove TransportData fallback
6. **Body pumps** — EmitDataFrames/EmitOwnedDataFrames else-branch removal
7. **Cleanup** — delete WireBufferWriter, WireBufferTestExtensions, dead Ops.OnOutbound paths

Each phase is independently shippable and testable.

## Risks / Trade-offs

**[Risk: Pipe buffer sizing]** → `TransportBufferWriter.GetMemory(sizeHint)` delegates to the Pipe's internal buffer management. If the Pipe's buffer is smaller than the encoded message, it may need to grow. Mitigation: the Pipe's default `minimumSegmentSize` is already tuned for HTTP frames; the existing `transport.GetMemory(buf.Length)` calls in `EmitWireBuffer` prove this works. No change in behavior.

**[Risk: H2 scratch buffer memory]** → The per-SessionManager scratch buffer stays allocated for the connection lifetime. For connections with rare large frames, this wastes memory. Mitigation: 64KB per connection is negligible; the current approach rents from `WireBuffer.SharedPool` per-write which is worse (pool contention + GC pressure from wrapper objects).

**[Risk: Test migration volume]** → ~100+ test files need mechanical changes. Mitigation: The pattern replacement is uniform (`WireBuffer.Rent → transport.Feed`, `ops.Outbound → transport.WrittenBytes`). Can be parallelized across protocol folders with haiku subagents.

**[Trade-off: H2 frame double-copy remains]** → SessionManager serializes into scratch buffer, then SM copies into pipe. This is one copy instead of the current two (SessionManager→WireBuffer→pipe), and avoids the `WireBuffer.Rent`/`Dispose` overhead. Eliminating this last copy would require passing `TransportBufferWriter` through the SessionManager API, which is a larger refactor (all frame `WriteTo` methods would need `IBufferWriter<byte>` overloads).

## Open Questions

None — all decisions are determined by existing architecture constraints.
