## Context

Phase D replaced the TCP transport's inbound data path with `System.IO.Pipelines` and introduced
`TransportIo` to manage the read loop from inside the SM. However, the outbound path was left on
the old `WireBuffer` → `TransportData` → `OnOutbound` route, with a `BridgeTransportData` workaround
in the stage logic copying data into the pipe. Only the H1.1 client SM was partially converted
(dual-path: direct pipe write when connected, `WireBuffer` fallback for pre-connect). The other five
TCP SMs still exclusively use the old path. This design extends `TransportIo` into a base class that
owns both the read and write paths, allowing every SM to write directly to the pipe.

## Goals / Non-Goals

**Goals:**
- Introduce `TcpStateMachineBase<TOps>` owning transport lifecycle, read loop, write path, async
  result routing, and cleanup.
- Convert all six TCP SM outbound paths to write directly to `PipeWriter` via the base class.
- Remove the `BridgeTransportData` workaround from both stage logics.
- Remove `TransportData` creation on the TCP outbound path.
- Remove dead `TransportDataFlushed` handling from H1.0/H1.1/H2 SMs.
- Achieve the Phase D design's original target: stage logic reduced to ~80 lines of pure plumbing.

**Non-Goals:**
- No QUIC/H3 changes.
- No encoder API changes (encoders still produce bytes; the base class bridges them to the pipe
  instead of the stage logic doing it).
- No changes to `IClientStateMachine` / `IServerStateMachine` interfaces (subclasses still implement
  them; the base class is an implementation detail).

## Decisions

### Decision 1: Base class shape

```csharp
internal abstract class TcpStateMachineBase<TOps>
{
    private readonly TransportIo _tio;       // read loop (existing, moved here)
    private TOps _ops;

    // --- Owned by base ---
    // Lifecycle dispatch: OnConnected, OnDisconnected
    // Read loop: TransportIo.RequestRead → Decode callback → AdvanceTo
    // Write path: GetMemory/Advance/FlushAsync on PipeWriter
    // Async result routing: OnAsyncResult filters ReadCompleted/FlushCompleted
    // Cleanup: TransportIo.Cleanup + subclass hook

    // --- Abstract / virtual for subclasses ---
    protected abstract (SequencePosition Consumed, SequencePosition Examined) DecodeData(
        ReadOnlySequence<byte> data);
    protected abstract void OnTransportConnected(ConnectionInfo info);
    protected abstract void OnTransportDisconnected(DisconnectReason reason);
    protected abstract void OnFlushCompleted();
    protected abstract void OnTransportLost(Exception? ex);
    protected virtual void OnCleanup() { }
}
```

The base class does NOT inherit from `IClientStateMachine` or `IServerStateMachine` — the concrete
SMs do. This avoids forcing a single dispatch shape on both client and server hierarchies.

### Decision 2: Two-tier hierarchy, not three

```
TcpStateMachineBase<TOps>
  ├── Http10ClientStateMachine : TcpStateMachineBase<IClientStageOperations>, IClientStateMachine
  ├── Http11ClientStateMachine : TcpStateMachineBase<IClientStageOperations>, IClientStateMachine
  ├── Http2ClientStateMachine  : TcpStateMachineBase<IClientStageOperations>, IClientStateMachine
  ├── Http10ServerStateMachine : TcpStateMachineBase<IServerStageOperations>, IServerStateMachine
  ├── Http11ServerStateMachine : TcpStateMachineBase<IServerStageOperations>, IServerStateMachine
  └── Http2ServerStateMachine  : TcpStateMachineBase<IServerStageOperations>, IServerStateMachine
```

No intermediate `ClientTransportBase` / `ServerTransportBase` layer. The shared client-specific logic
(reconnect, request queue) and server-specific logic (completion, body resume) stay in each SM —
they vary enough across protocols that a shared intermediate adds more coupling than it removes.
If a pattern emerges later, an intermediate can be extracted without changing the base class contract.

### Decision 3: Write path — base class exposes transport, SM writes directly

The base class exposes `protected IConnectionTransport? Transport` (non-null when connected). The SM
writes directly:

```csharp
// In SM encode method:
if (Transport is { } t)
{
    var mem = t.GetMemory(size);
    encoder.WriteTo(mem.Span);
    t.Advance(size);
    RequestFlush();  // base class method
}
```

`RequestFlush()` is a base class method that calls `_tio.RequestFlush()` — one flush tracking
location instead of two (the current bridge has a separate `_flushInProgress` in the stage logic).

For pre-connect buffering (client SMs only), the SM continues to use
`_ops.OnOutbound(TransportData.Rent(buf))` for the narrow window between `OnRequest` and
`TransportConnected`. This is acceptable because:
1. It only fires for the very first request on a connection (before any transport exists).
2. The stage logic's `_outboundQueue` already handles this correctly for lifecycle commands.
3. Converting pre-connect to an internal buffer in the base class is orthogonal and can follow later.

### Decision 4: Stage logic bridge removal

After all SMs write directly to the pipe:
1. `BridgeTransportData` method → deleted
2. `BridgeFlushCompleted` / `BridgeFlushFailed` message types → deleted
3. Stage logic `_flushInProgress` / `_flushGen` → deleted
4. `OnOutbound`: removes the `if (item is TransportData td && _transport is not null)` branch —
   all items go to the network port (lifecycle commands only on TCP)
5. `PostStop`: removes `TransportData` disposal from `_outboundQueue` drain — queue no longer
   contains `TransportData` items
6. Stage logic `_transport` field → deleted (SM owns transport reference via base class)

### Decision 5: Encoder integration — no API change

Encoders (`Http11ClientEncoder`, `Http2ClientSessionManager.EmitFrame`, etc.) currently produce
`WireBuffer` objects. This change does NOT refactor the encoder API to accept `IBufferWriter<byte>`.
Instead, the SM-level encode call sites change from:

```csharp
// Before:
var buf = WireBuffer.Rent(size);
encoder.WriteTo(buf);
_ops.OnOutbound(TransportData.Rent(buf));

// After:
if (Transport is { } t)
{
    var buf = WireBuffer.Rent(size);
    encoder.WriteTo(buf);
    var mem = t.GetMemory(buf.Length);
    buf.Span.CopyTo(mem.Span);
    t.Advance(buf.Length);
    buf.Dispose();
    RequestFlush();
}
else
{
    var buf = WireBuffer.Rent(size);
    encoder.WriteTo(buf);
    _ops.OnOutbound(TransportData.Rent(buf));  // pre-connect only
}
```

The copy from `WireBuffer` → pipe memory stays for now (same as the bridge did, but without
the `TransportData` wrapper allocation). A follow-up change can refactor encoders to write to
`IBufferWriter<byte>` directly, eliminating the `WireBuffer.Rent` entirely — but that is a
separate, larger change that touches every encoder and their tests.

**Exception: H1.1 client SM** — already writes directly to `transport.GetMemory` for body data
via `TransportBufferWriter`. That code stays as-is and serves as the model for future encoder
refactoring.

### Decision 6: TransportDataFlushed removal

`TransportDataFlushed` was the credit signal for the serial body pump under the old
`Channel<WireBuffer>` transport. With pipe-based transport, the serial body pump's credit comes from
`TransportIo.RequestFlush` completing (via `onFlushCompleted` callback). The
`case TransportDataFlushed:` branches in `DecodeServerData`/`DecodeClientData` are dead on TCP
(no source emits this event since Phase D removed it from the transport stage). Remove them.

Verify: `TransportDataFlushed` is still emitted by QUIC (`MultiplexedDataFlushed` is the QUIC
equivalent, but `TransportDataFlushed` itself is listed in `ITransportInbound` — check whether
any QUIC code actually emits `TransportDataFlushed` before removing the class). If QUIC doesn't
use it, the class can be deleted from `ITransportInbound.cs` as well.

### Decision 7: Phased rollout per SM

Convert one SM at a time, verifying the test suite after each:
1. H1.1 client (easiest — already partially converted)
2. H1.0 client (simplest protocol)
3. H1.1 server
4. H1.0 server
5. H2 client (most complex — session manager + flow control)
6. H2 server

After all six SMs: remove the bridge from stage logic. After bridge removal: audit pass for dead
code (`TransportDataFlushed`, orphaned test helpers, dead imports).

## Risks / Trade-offs

**[Base class coupling]** → Six SMs now share a base class they didn't before. A change to the
base class read/write path affects all six protocols. Mitigation: the base class contract is narrow
(5 abstract methods) and the shared functionality (lifecycle dispatch, read loop, flush) was already
identical across all SMs — it was just copy-pasted instead of shared. The risk of divergence is
lower than the risk of the current copy-paste.

**[Pre-connect buffering still uses TransportData]** → Client SMs still emit `TransportData` for the
narrow pre-connect window. This means `TransportData` cannot be fully removed from the TCP path yet.
Mitigation: the pre-connect path is exercised by exactly one scenario (first request on a fresh
connection before `TransportConnected`); it's well-tested and doesn't affect steady-state
performance. A follow-up can internalize pre-connect buffering in the base class.

**[Extra copy remains until encoder refactor]** → The `WireBuffer.Rent` → pipe copy is still
present (Decision 5). This is identical to what the bridge did, minus the `TransportData` wrapper
allocation. The full elimination requires refactoring encoders to `IBufferWriter<byte>`, which is
a separate change. Net effect: one fewer allocation per outbound packet (`TransportData.Rent`
eliminated), same number of copies.
