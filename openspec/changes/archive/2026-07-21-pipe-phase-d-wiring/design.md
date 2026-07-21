## Context

Phase A gave us `IConnectionTransport` + pump tasks, unwired. Phase B gave FrameDecoders a
`ReadOnlySequence<byte>` input. Phase C gave the protocol SMs `RequestRead`/`RequestFlush` against
`IConnectionTransport`, but kept the legacy `TransportData` port path alive as a fallback so Phase C
could land without `TcpConnectionStateMachine` switching modes yet. Phase D removes the fallback and
flips the transport stage over. See `openspec/changes/pipe-transport-tcp/design.md` for the full
end-state rationale (Decisions 1–11); this document covers only the wiring-specific decisions Phase D
adds.

## Goals / Non-Goals

**Goals:**
- `TcpConnectionStateMachine` creates real pipes + pumps on lease acquisition and pushes
  `TransportConnected(IConnectionTransport)`.
- `HttpClientConnectionStageLogic` / `HttpServerConnectionStageLogic` shrink to plumbing only.
- Reconnect produces a clean new pipe/pump/transport generation, tearing down the old one.
- Pump task failure is observable as `TransportDisconnected`, not a silent hang.
- Full test suite (unit+stage, integration, submodule) passes with pipe mode as the only TCP path.

**Non-Goals:**
- No QUIC/H3 changes.
- No new backpressure knobs beyond the thresholds fixed in Phase A.
- No change to the `IConnectionTransport` interface surface.

## Decisions

### Decision 1: Transport stage creates pipes on lease acquisition

`TcpConnectionStateMachine.OnLeaseAcquired` is the single creation point for a connection's pipe pair:

```
OnLeaseAcquired(lease):
  input  = new Pipe(inputPipeOptions)   // pause 128KB / resume 64KB
  output = new Pipe(outputPipeOptions)  // pause 256KB / resume 128KB
  readPumpTask  = TransportPumps.RunReadPump(lease.Socket, input.Writer, _adaptiveHint, _ct)
  writePumpTask = TransportPumps.RunWritePump(output.Reader, lease.Socket, _ct)
  transport = new ConnectionTransport(input.Reader, output.Writer, connectionInfo)
  _generation++
  Push(TransportConnected(transport))
```

No `Channel<WireBuffer>`, no `_bytesInFlight` watermark state, no `OnFlushed` callback — those fields
are removed from `TcpConnectionStateMachine` for the TCP path (this was deferred from Phase A/B/C
specifically so it could land together with the stage-logic thinning in one reviewable unit).

### Decision 2: Pump tasks monitored via PipeTo, not fire-and-forget

```
readPumpTask.PipeTo(Self, success: _ => new PumpCompleted(PumpKind.Read, generation),
                          failure: ex => new PumpFailed(PumpKind.Read, generation, ex))
writePumpTask.PipeTo(Self, success: _ => new PumpCompleted(PumpKind.Write, generation),
                           failure: ex => new PumpFailed(PumpKind.Write, generation, ex))
```

Either pump completing (EOF/error) means the connection is no longer usable — the SM reacts by
completing the *other* pipe (so its pump also unwinds), aborting the transport, and emitting
`TransportDisconnected`. Generation-tagging follows the same pattern as `_connectionGen` elsewhere in
the transport SM: a `PumpCompleted`/`PumpFailed` for a stale generation (already superseded by
reconnect) is dropped.

### Decision 3: Stage logic reduced to ~80 lines

`HttpClientConnectionStageLogic` (and the server equivalent) keep exactly three responsibilities:

1. **Port handlers** — `onPush(_inNetwork)`: grab the lifecycle item, call `_sm.OnTransportEvent(item)`,
   pull again. `onPull(_outRequest/_outResponse)`: unchanged pull-through to the SM's queue. No branch
   on item type beyond `TransportConnected`/`TransportDisconnected` — `TransportData` is no longer a
   member the TCP stages need to pattern-match.
2. **StageActor message routing** — every message the StageActor receives (`ReadCompleted`,
   `FlushCompleted`, `PumpCompleted`, body-pump messages) is routed to `_sm.OnAsyncResult(msg)`
   unopened; the SM owns interpreting the payload.
3. **Timer delegation** — `OnTimer(key)` calls `_sm.OnTimer(key)` (unchanged from Phase C).

Everything that used to live in the stage logic — decode dispatch, outbound queueing, flush-credit
bookkeeping — has already moved to the SM in Phase C. Phase D's job is deleting the now-dead branches
(the `TransportData` grab/push paths) rather than adding new logic, which is why the target size is a
reduction (roughly 80 lines of live port/actor/timer plumbing, down from the pre-Phase-C stage logic).

### Decision 4: Server `CompleteAfterFlushingOutbound` via `CompleteOutput()`

Before (queue drain):
```
_completeAfterFlush = true
while (_outboundQueue.Count > 0 && IsAvailable(_outNetwork))
    Push(_outNetwork, _outboundQueue.Dequeue())
// CompleteStage() once queue empties and Push completes
```

After (pipe-native):
```
_transport.CompleteOutput()          // PipeWriter.Complete() — write pump drains remaining buffered
                                      // bytes to the socket, then the pump task itself completes
// stage awaits PumpCompleted(Write) via StageActor, then CompleteStage()
```
Completion is no longer polled through the Akka Streams port at all — it is driven by the pipe's own
flush-then-complete sequencing, observed via the same `PipeTo(Self)` pump-monitoring path as Decision 2.
This removes the GOAWAY-flush-then-close race that previously depended on `IsAvailable(_outNetwork)`
timing.

### Decision 5: Reconnect creates a full new pipe/pump/transport generation

```
OnReconnect():
  old_transport.Abort()              // idempotent if pumps already completed
  input.Writer.Complete(); output.Reader.Complete()   // in case pumps are still draining
  <Decision 1 sequence again, with _generation incremented>
```

The SM's existing `_transportGen` guard (Phase C) already drops stale `ReadCompleted`/`FlushCompleted`
messages from the old generation; Decision 2's pump-monitoring generation tag closes the same gap for
`PumpCompleted`/`PumpFailed`.

## Risks / Trade-offs

**[First real end-to-end exercise]** → Phases A–C were each independently tested with mocks/fakes;
Phase D is the first time real sockets, real pumps, and the full SM/stage stack run together, including
under reconnect churn. Mitigation: green criterion is explicitly the full suite (unit+stage, integration
×3, submodule), not a subset — see tasks.md.

**[Dual-path removal is a one-way door]** → Deleting the Phase C `TransportData` fallback means any
latent bug in the pipe path has no safety net. Mitigation: land pipe-mode wiring and dual-path removal
as separate, sequentially-verified tasks (wire first, verify green, then delete fallback, verify green
again) rather than one combined commit.

**[Cherry-picked bug fixes touch code Phase D is also rewriting]** → PendingRequest stale-cancel and
reconnect body truncation fixes from `feat/outbound-flow-control` touch the same SM reconnect paths
Decision 5 changes. Mitigation: cherry-pick and adapt those fixes onto the pipe-based reconnect flow
explicitly (not a blind cherry-pick) — see tasks.md item on bug fix integration.
