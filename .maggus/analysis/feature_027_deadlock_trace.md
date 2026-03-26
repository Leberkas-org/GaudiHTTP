# Feature 027: Deadlock Trace Analysis

## Summary

5 batch runs of 78 H10 integration tests were executed with `TURBO_DEBUG=1` diagnostic logging enabled. The deadlock pattern is **100% consistent** across all affected test categories (Retry, Cache, Compression). A separate deterministic bug (Error-H10-005) also appears in every run.

## Failure Summary Across 5 Runs

| Run | Failures | Deadlocked Tests | Error-H10-005 |
|-----|----------|-----------------|---------------|
| 1 | 3 | Retry-H10-001 (15s), Retry-H10-004 (15s) | Yes (6ms) |
| 2 | 2 | Retry-H10-006 (15s) | Yes (6ms) |
| 3 | 5 | Cache-H10-004 (10s), Cache-H10-005 (10s), Retry-H10-002 (15s), Retry-H10-006 (15s) | Yes (7ms) |
| 4 | 5 | Compression-H10-005 (10s), Cache-H10-011 (10s), Retry-H10-004 (15s), Retry-H10-007 (15s) | Yes (6ms) |
| 5 | 1 | (none) | Yes (6ms) |

**Total deadlocked tests across 5 runs: 12** (random distribution across Retry, Cache, Compression categories)

## Two Distinct Issues

### Issue 1: Error-H10-005 — Deterministic Failure (NOT a deadlock)

- Fails in 6-7ms every single run — this is an assertion failure, not a timeout
- Likely a test logic or route configuration bug
- Separate from the deadlock investigation

### Issue 2: Pipeline Deadlock — Sporadic, Multi-Category

Affects any test that requires **2+ sequential HTTP/1.0 requests on the same client** (retry after error, cache revalidation, compression negotiation). Tests requiring only 1 request never deadlock.

## The Deadlock Pattern

### Consistent Sequence (verified across Retry, Cache, and Compression tests in all 5 runs)

The pattern is identical for every deadlocked test, regardless of category. Here is the annotated lifecycle from Supervisor-63 in Run 3 (a Cache revalidation test):

#### Phase 1: First Request (succeeds)
```
ExtractOptionsStage: onPush request=GET /cache/no-cache, connectItemSent=False, needsReconnect=False
ExtractOptionsStage: emitting ConnectItem for 127.0.0.1:64143
TcpTransport: HandleConnectItem key=127.0.0.1:64143
TcpTransport: _onLeaseAcquired gen=1
ConnectionStage: HandlePush DataItem length=69
TcpTransport: HandleDataItem writing length=69
ConnectionStage: PushOutput DataItem (direct)
ConnectionReuseStage: onPush response status=200, canReuse=False, endpoint=127.0.0.1:64143
ConnectionReuseStage: pushing signal canReuse=False
ExtractOptionsStage: InReuse received canReuse=False → _needsReconnect set to true
TcpTransport: HandleConnectionReuseItem canReuse=False, upstreamFinished=False
TcpTransport: StopInboundPump gen=2
ConnectionStage: SignalPullInput — pulling inlet
TcpTransport: _onInboundComplete gen MISMATCH (stale=1, current=2) — ignored
```

#### Phase 2: Second Request (re-injected by feature stage — succeeds)
```
GroupByHostKeyStage: routed to existing substream key=127.0.0.1:64143
ExtractOptionsStage: onPush request=GET /cache/no-cache, connectItemSent=True, needsReconnect=True
ExtractOptionsStage: emitting ConnectItem for 127.0.0.1:64143  ← reconnection!
TcpTransport: Cleanup gen=2
TcpTransport: HandleConnectItem key=127.0.0.1:64143
TcpTransport: _onLeaseAcquired gen=1
ConnectionStage: HandlePush DataItem length=69
TcpTransport: HandleDataItem writing length=69
ConnectionStage: PushOutput DataItem (direct)
ConnectionReuseStage: onPush response status=200, canReuse=False
ExtractOptionsStage: InReuse received canReuse=False → _needsReconnect set to true
TcpTransport: HandleConnectionReuseItem canReuse=False, upstreamFinished=False
TcpTransport: StopInboundPump gen=2
ConnectionStage: SignalPullInput — pulling inlet
TcpTransport: _onInboundComplete gen MISMATCH (stale=1, current=2) — ignored
```

#### Phase 3: DEADLOCK — GroupByHostKeyStage kills the substream
```
GroupByHostKeyStage: completing all 1 substreams  ← SUBSTREAM COMPLETED
GroupByHostKeyStage: completing all 0 substreams  ← (4x repeated — cascading completion)
```

#### Phase 4: Silence (10-15 seconds)

No pipeline activity. The feature stage (Cache/Retry/Compression BidiStage) is waiting to re-inject a 3rd request, but the substream it needs to push into is dead.

#### Phase 5: Timeout and Cleanup
```
[WARNING] ContentEncodingBidiStage: Response upstream failure absorbed:
    Processor actor [...] terminated abruptly
TcpTransport: Cleanup gen=2
```

### Which Stage Is Waiting (Last Log Entry Before Hang)

**`ConnectionStage: SignalPullInput — pulling inlet`** is the last stage activity log before the hang. Immediately after, `GroupByHostKeyStage: completing all 1 substreams` fires on a different thread, killing the substream.

### What It's Waiting For

The pipeline is waiting for a **3rd request to flow through the substream**. The feature BidiStage (Retry/Cache/Compression) has determined it needs to re-send a request (retry after 503, revalidation with If-None-Match, etc.) and pushes it back upstream. But the substream is already completed by GroupByHostKeyStage.

### What Should Have Provided It

The **GroupByHostKeyStage** should NOT have completed the substream while the pipeline still has pending work. The substream completion is premature — it fires because the ChannelSource upstream has no more items queued (the next request hasn't been re-injected yet by the feature stage).

### Why This Is Sporadic

The race condition is between:
1. **Feature BidiStage** processing the response and deciding to re-inject a request (pushes back upstream)
2. **GroupByHostKeyStage** receiving upstream completion signal from the ChannelSource

When the feature stage re-injects fast enough, the substream stays alive and the 3rd request succeeds. When there's thread scheduling jitter (more likely under load with 78 tests sharing one ActorSystem), the completion signal wins the race and the substream dies.

## Root Cause

**GroupByHostKeyStage prematurely completes substreams when the upstream ChannelSource reports completion between HTTP/1.0 request cycles.**

In HTTP/1.0, every response closes the connection (`canReuse=False`). The pipeline handles this correctly via the reconnection cycle (ExtractOptionsStage emits a new ConnectItem). However, there's a timing window where:

1. The ChannelSource has delivered all currently-queued requests
2. GroupByHostKeyStage sees upstream completion and kills all substreams
3. The feature BidiStage hasn't yet re-injected the next request

This maps to **Hypothesis H1 (GroupByHostKey Substream Death)** from the feature document — confirmed.

## Hypothesis Verdict

| Hypothesis | Verdict | Evidence |
|-----------|---------|---------|
| H1: GroupByHostKey Substream Death | **CONFIRMED** | Every deadlock trace ends with `completing all N substreams` immediately before the hang |
| H2: MergePreferred Demand Stall | Unlikely primary cause | MergePreferred stages operate correctly in the traces — demand propagates normally during successful cycles |
| H3: ConnectionPool Lease Leak | Not observed | Each test creates its own pool; no evidence of semaphore exhaustion |
| H4: Inbound Pump Thread Leak | Not observed | `_onInboundComplete gen MISMATCH` is correctly handled; pump tasks don't accumulate |

## Recommendations for TASK-027-003

The fix should ensure that **GroupByHostKeyStage does not complete substreams while feature BidiStages may still have pending re-injection work**. Possible approaches:

1. **Don't propagate upstream completion through GroupByHostKey until all substreams have drained** — the substream should only complete when both upstream is done AND no downstream stage has pending re-injections
2. **Use a completion barrier** — feature BidiStages that re-inject requests should signal "busy" to prevent premature substream completion
3. **Re-create substreams on demand** — if a substream was completed but a new request arrives for the same host-key, create a fresh substream instead of failing

Option 3 is the most resilient and aligns with how HTTP/1.0 works (each request is independent).
