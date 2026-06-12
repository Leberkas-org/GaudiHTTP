---
status: fixed
component: Client
discovered: '2026-06-12'
fixed: '2026-06-12'
branch: release-next
severity: high
tags:
  - bug
  - client
  - race-condition
  - fixed
---
# SendAsync Mutated request.Options After Enqueue (Dictionary Corruption Race) — FIXED (2026-06-12)

## Symptom

Full benchmark run, `KestrelTurboSendAsyncConcurrentBenchmarks` H2 CL=4096: benchmark child
process crashed (exit -1) with

```
InvalidOperationException: Operations that change non-concurrent collections must have
exclusive access. ... at RequestEnricher.Enrich (line 88, Options.TryGetValue)
→ MergeHub ProducerFailed → consumer ingress dies
```

## Root cause

`TurboHttpClient.SendAsync` wrote the request into the channel (`Requests.WriteAsync`) and
**afterwards** called `request.SetCancellationToken(cts.Token)` → `request.Options.Set(...)`
on the caller thread. From the moment the request is enqueued, the pipeline's
`RequestEnricher.Enrich` reads and mutates the same `HttpRequestOptions` (a plain
`Dictionary<string, object?>`) on a MergeHub stream thread. Two unsynchronized writers →
dictionary state corruption under high concurrency (CL=4096 reliably hit the window).

## The fix

Reordered `SendAsync`: the CTS is created (linked / pooled / fresh) and the cancellation
token stamped into `request.Options` **before** `Requests.WriteAsync`. `CancelAfter` and the
`UnsafeRegister` callback still happen after the write (they don't touch Options). Cleanup
(TryReset/pool/dispose) moved to the outer finally so the early-created CTS is also released
when the channel write throws.

**Invariant to preserve:** nothing may touch `request.Options` after the channel write —
the options dictionary is single-owner until enqueue, then owned by the stream side.

## Tests

- `TurboHttpClientSpec.SendAsync_should_set_cancellation_token_before_enqueueing` — a
  capturing `ChannelWriter` asserts the token is present at the moment of `TryWrite`
  (deterministic; failed pre-fix).
