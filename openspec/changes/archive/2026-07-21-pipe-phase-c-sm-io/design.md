## Context

`pipe-transport-tcp/design.md` (the master plan) lays out the end state: the protocol SM owns the
transport read/write loop, `IConnectionTransport` replaces `TransportData`/`WireBuffer` on the wire, and
`HttpClientConnectionStageLogic`/`HttpServerConnectionStageLogic` shrink to pure Akka Streams plumbing.
Phase A delivered `IConnectionTransport` + pumps in isolation (nothing consumes it). Phase B made
`FrameDecoder` accept `ReadOnlySequence<byte>` (still fed from `WireBuffer`-derived memory today). Phase C
is the SM-side half of the master plan's Decisions 2, 5, 7, and 9 — built and tested against a mock
transport, with the stage/transport side of the wiring deferred to Phase D.

The key constraint that does not exist in the master plan's end-state design: Phase C ships while
production traffic still flows through `TransportData` on the port, because the transport stage hasn't
switched yet. Every SM entry point touched in this phase needs a legacy fallback, not just a pipe-mode
implementation.

## Goals / Non-Goals

**Goals:**
- Give SMs the read/write loop, generation guard, and `PipeTo(ops.Self)` bridging described in the master
  design, working against a mock `IConnectionTransport` in unit tests.
- Refactor encoders to `IBufferWriter<byte>` so the same encode call serves pipe mode and (via an adapter)
  legacy `WireBuffer` mode.
- Adapt `SerialBodyPump`/`FlowControlledBodyPump` chunk delivery to have a pipe-mode branch.
- Keep the legacy path's observable behavior byte-for-byte identical — existing SM/stage/integration tests
  must pass unmodified.

**Non-Goals:**
- Switching the transport stage to pipe mode (Phase D).
- Removing `TransportData`, `TransportDataFlushed`, or the watermark credit system (Phase D/E).
- Thinning `HttpClientConnectionStageLogic`/`HttpServerConnectionStageLogic` beyond adding `Self` (Phase D).
- Any H3/QUIC change.

## Decisions

### Decision 1: Dual-path via a single `_transport != null` branch, not two code paths per method

Every touched method gets exactly one branch point:

```
if (_transport != null)
    // pipe mode: ReadAsync / GetMemory+Advance / FlushAsync
else
    // legacy: existing DecodeServerData(TransportData) / ops.OnOutbound(TransportData.Rent(...))
```

`_transport` is set from `TransportConnected.Transport` (the optional member Phase A added) when present,
and stays null otherwise — so today's transport stage (which never sets it) exercises the legacy branch
unconditionally, and unit tests that construct a mock `IConnectionTransport` and push it in exercise the
pipe branch. No feature flag, no configuration switch — presence of the transport is the switch.

**Why not two parallel SM implementations:** would double the surface area to review and double-maintain
until Phase D deletes one of them. A single branch point per method keeps the diff smaller and makes the
eventual Phase D deletion (delete the `else`, unindent the `if` body) mechanical.

### Decision 2: SM owns the read/write loop, Stage provides Self (unchanged from master plan)

Same as `pipe-transport-tcp/design.md` Decision 2: the SM calls `_transport.ReadAsync()` /
`_transport.FlushAsync()` directly and bridges async completions via `vt.PipeTo(_ops.Self, ...)`. `Self` is
added to `IClientStageOperations`/`IServerStageOperations` now (Phase C) even though the stage doesn't
route `OnAsyncResult` dispatch to anything meaningful in production yet — the plumbing exists so Phase C's
unit tests can drive it end-to-end against a `TestProbe`-backed `Self`.

### Decision 3: Sync fast-path with budget = 8 (unchanged from master plan)

Same shape as master plan Decision 5 and the existing `TcpConnectionStateMachine`/`ReadEventState`
pattern already in servus.akka:

```
RequestRead():
  if (_transport == null) return;               // legacy: nothing to do, port drives reads
  if (ShouldPauseNetwork) return;
  var vt = _transport.ReadAsync(ct);
  if (vt.IsCompletedSuccessfully && _syncReadBudget > 0)
    _syncReadBudget--; OnReadCompleted(vt.Result); RequestRead();
  else
    _syncReadBudget = MaxSyncReads;
    vt.PipeTo(_ops.Self, success: _readState.Success, failure: _readState.Failure);
    _readInProgress = true;
```

`RequestFlush()` mirrors this for `FlushAsync()`, using `PipeFlushState`.

### Decision 4: Cached delegate holders scoped per transport generation

`PipeReadState` / `PipeFlushState` hold the `Func<ReadResult, object>` / `Func<FlushResult, object>` /
`Func<Exception, object>` transforms used by `PipeTo`, closing over the current `_transportGen` so the
resulting `ReadCompleted`/`FlushCompleted` messages carry the generation without a per-call allocation.
New instances are created only on `TransportConnected` (new generation), not on every read/flush — same
allocation profile as the master plan's Decision 9 generation guard and the existing `ReadEventState`
pattern this mirrors.

### Decision 5: Encoder refactor is mode-agnostic; legacy path gets a thin adapter

Encoders change their write target from `WireBuffer`/`SpanWriter` to `IBufferWriter<byte>`. In pipe mode,
`IConnectionTransport` (or a thin wrapper exposing `GetMemory`/`Advance` as `IBufferWriter<byte>`) is
passed directly. In legacy mode, a `WireBufferWriter : IBufferWriter<byte>` adapter wraps the rented
`WireBuffer` so the SM can call the identical encoder method and then wrap the adapter's written span back
into a `TransportData` for `ops.OnOutbound`. This means the encoder method itself has no branch —
`Http11ClientEncoder.Encode(HttpRequestMessage, IBufferWriter<byte>)` is the single signature both modes
call.

**Why not keep two encoder methods:** the master plan's end state has exactly one encoder signature.
Building the adapter now means Phase D deletes the adapter and the legacy call site, not the encoder
method — same "delete the else" mechanical removal as Decision 1.

### Decision 6: Body pump chunk delivery forks at the write call site, not inside the pump

`SerialBodyPump`/`FlowControlledBodyPump` keep their existing scheduling/credit logic untouched (H2's
WINDOW_UPDATE gating in particular — Decision applies only to the wire-write step, per the master plan's
task 9.3). The SM's `OnBodyMessage` handler, when it receives a chunk from the pump, does:

```
if (_transport != null)
    encoder.WriteChunk(chunk, _transport); _transport.RequestFlush-equivalent...
else
    ops.OnOutbound(TransportData.Rent(encodedChunk));
```

`SerialBodyPump`'s pipe-mode capacity signal is `_flushInProgress` (set false on `OnFlushCompleted`)
instead of `TransportDataFlushed`/`OutboundBodyCapacity` — but the legacy credit system (`_bytesInFlight`,
watermarks) keeps running unmodified for the legacy branch, exactly as the master plan's flow-control
delta describes it as removed only when TCP-based *and pipe-mode active*, not unconditionally.

### Decision 7: Generation guard applies to both branches uniformly

`_transportGen` increments on every `TransportConnected`, pipe mode or not — this keeps the counter
meaningful once Phase D removes the legacy branch, and costs nothing extra: the legacy branch already
receives `TransportConnected` via `DecodeServerData`/`DecodeClientData` today, so incrementing there is a
one-line addition to an existing handler.

## Risks / Trade-offs

**[Branch proliferation across 7 SMs]** → every touched method needs a null-check on `_transport`.
Mitigation: keep the branch shape identical everywhere (`if (_transport != null) { pipe } else { legacy
unchanged }`) so it's mechanically greppable and removable in Phase D; put the shared parts (`RequestRead`,
`OnAsyncResult`, generation guard) in one place (shared base or shared helper struct) rather than
duplicating per-SM.

**[Unit-tested-only pipe path has no production exercise until Phase D]** → bugs in the pipe branch won't
surface until the stage switch lands. Mitigation: unit tests in this phase must cover the pipe branch as
thoroughly as integration tests would (sync fast-path, async PipeTo, generation guard, `ShouldPauseNetwork`
interaction, flush backpressure) using a scriptable mock `IConnectionTransport`, per master plan task 4.8–4.10.

**[Encoder adapter (`WireBufferWriter`) is throwaway code]** → written in Phase C, deleted in Phase D once
the legacy `WireBuffer` path is gone. Mitigation: acceptable — the alternative (forking the encoder method
itself) creates two independent bodies to test and keep in sync for the same duration, which is worse.

**[H2 body pump WINDOW_UPDATE gating must not be touched]** → flow-control window bookkeeping is
correctness-critical and unrelated to the wire-write mechanism. Mitigation: only the write call site
inside `OnBodyMessage`/the read-completion callback forks; `FlowControlledBodyPump.Reserve`/`Refund`/
`OnWindowUpdate` are untouched, verified by the existing flow-control unit test suite passing unmodified.
