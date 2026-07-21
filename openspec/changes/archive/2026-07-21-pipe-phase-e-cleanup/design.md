## Context

By the end of Phase D, `IConnectionTransport` (Pipe-backed) is the only TCP data path exercised by
production traffic and by the full test suite. Everything this phase removes is code that Phase D's
dual-path deletion already made unreachable on TCP — it is still compiled and, in a few cases, still
allocates (e.g. `_bytesInFlight` bookkeeping runs even though nothing reads it), but no test path and no
runtime path can reach it. This is a mechanical dead-code sweep, not a design change: there is no new
behavior, no new abstraction, and no decision left open that Phases A–D didn't already settle.

## Goals / Non-Goals

**Goals:**
- Delete TCP-only call sites of `WireBuffer`, `TransportData`, `TransportDataFlushed`, the outbound
  channel, the watermark credit fields, `SendFlushed`/`OnFlushed`, `ReadEventState`, and the legacy
  `ReceiveAsync` loop.
- Leave every class QUIC still needs (`WireBuffer`, `TransportData`, `ITransportInbound`,
  `ITransportOutbound`) in place, untouched in shape.
- Leave zero dead imports, unused fields, or orphaned test doubles behind.

**Non-Goals:**
- No QUIC/H3 changes.
- No new abstraction or interface change — this phase does not touch `IConnectionTransport`'s shape.
- No behavior change on any currently-green test. If a test goes red, that test was depending on
  "dead" code that wasn't actually dead — stop and re-classify it as in-scope for a follow-up, don't
  force the removal through.

## Decisions

### Decision 1: What stays vs. what goes

| Stays (QUIC needs it) | Goes (TCP-only, now unreachable) |
|---|---|
| `WireBuffer` class | `WireBuffer.Rent` call sites in TCP send/receive |
| `TransportData` class | `TransportData` creation/dispatch on the TCP network port |
| `ITransportInbound` / `ITransportOutbound` | — (interfaces unchanged; only TCP implementations of the data-carrying cases go) |
| H2 WINDOW_UPDATE flow control | `TransportDataFlushed` emission/handling for TCP, `SendFlushed`, `OnFlushed` |
| H3 `MultiplexedDataFlushed` | TCP `_bytesInFlight`/`_highWatermark`/`_lowWatermark` |
| — | `ReadEventState` (replaced by `PipeReadState`/`PipeFlushState`) |
| — | old `ReceiveAsync(WireBuffer)` + `PipeTo(ReadCompleted)` loop (replaced by read pump) |
| — | `Channel<WireBuffer>` outbound queue (replaced by write pump + `IConnectionTransport.FlushAsync`) |

### Decision 2: DuplexConnectionBase handling

If `DuplexConnectionBase` is shared between TCP and QUIC (verify at implementation time — Phase A/D
didn't need to split it because the TCP-only members were merely unused, not yet deleted), the
`Channel<WireBuffer>`/`SendFlushed`/`OnFlushed` members are TCP-only and must not be deleted wholesale if
QUIC's `DuplexConnectionBase` subclass still calls them. Two options, pick whichever the actual code
supports with less churn:
1. **Split**: extract the TCP-only channel/callback members into a `TcpDuplexConnection`-only
   partial/subclass, leaving `DuplexConnectionBase` with only the members both transports use.
2. **Conditional**: keep the members on the base but confirm (via reference search, not assumption) that
   QUIC's subclass never wires `OnFlushed`/`SendFlushed`/the channel — in which case they can be deleted
   from the base outright.
Resolve this with a reference search (`find_references`/`find_callers` on `SendFlushed`, `OnFlushed`,
the `Channel<WireBuffer>` field) before deleting, not by inspection alone — QUIC's actual usage determines
which option applies.

### Decision 3: ops.OnOutbound — keep lifecycle, remove data

`IClientStageOperations.OnOutbound`/`IServerStageOperations.OnOutbound` currently has (at least) two call
shapes: pushing lifecycle commands (`DisconnectTransport`, `ConnectTransport`) and pushing raw byte data
(`TransportData`) — the latter is dead on TCP since Phase D moved data writes to `_transport.GetMemory`/
`Advance`/`FlushAsync` directly. Remove only the data-carrying overload/call sites; keep the
lifecycle-command overload as-is. If H3/QUIC still routes `MultiplexedData` through `OnOutbound`, that
call site is explicitly out of scope and stays.

### Decision 4: Audit pass — how to find what's actually dead

Don't hand-search. For each candidate removal (`WireBuffer` TCP call sites, `TransportData` TCP dispatch,
`ReadEventState`, `_bytesInFlight` etc.), use `find_references`/`find_callers` (Roslyn navigator) to
confirm zero remaining TCP-path callers before deleting, and re-run to confirm zero *any* callers after —
catching both under-deletion (missed a call site) and over-deletion (deleted something QUIC still uses).
`find_dead_code` is a useful first pass to generate the candidate list, but confirm each one — it can
flag things that are reachable only through Akka message dispatch (not a direct C# call graph edge) as
falsely dead.

## Risks / Trade-offs

**[Shared base class deletion breaks QUIC]** → Deleting a `DuplexConnectionBase` member without
confirming QUIC doesn't call it silently breaks the QUIC transport (compiles fine if QUIC doesn't
reference the member by name in a way the compiler catches — e.g. an interface implementation gap).
Mitigation: Decision 2's reference-search-first rule; QUIC integration tests must run in the final
verification pass even though this phase claims "TCP only" in its proposal.

**["Dead" code turns out to have a live caller from a test helper**] → Test infrastructure sometimes
exercises paths production code no longer reaches (e.g. a stage test that still scripts `TransportData`
directly instead of going through the transport). Mitigation: when a removal breaks a test, first
determine whether the test is testing the deleted TCP path specifically (then delete/update the test) or
exercising shared plumbing that QUIC also needs (then the removal was wrong — stop, per the Stop Stacking
Workarounds principle: don't add a compatibility shim, re-scope the removal).

**[Removing `_outboundQueue` data items regresses ordering]** → If any remaining lifecycle command
ordering implicitly depended on data items also being in the same queue (e.g. "flush pending data before
this disconnect command"), removing the data items could reorder lifecycle effects. Mitigation: lifecycle
commands were already ordered independently of data since Phase D moved data off the queue's hot path;
confirm via the stage-level tests exercising disconnect timing, not by inspection.
