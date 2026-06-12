---
status: fixed
component: Protocol/Http11/Server
discovered: '2026-06-12'
fixed: '2026-06-12'
branch: release-next
severity: critical
tags:
  - bug
  - http11
  - http10
  - server
  - data-rate
  - connection-reset
  - fixed
---
# H1.x Server Killed Idle Keep-Alive Connections (Response-Rate Entry Leak) — FIXED (2026-06-12)

## Symptom

The four zero-result rows in the 2026-06-12 server benchmark report (H1.1
Plaintext/Fortunes/Upload_Concurrent): BenchmarkDotNet cases failed with
`SocketException 10054: connection forcibly closed by the remote host`. GET benchmarks
mostly survived because SocketsHttpHandler silently retries requests that die on a reused
connection before the response starts (BDN still measured 0.77 first-chance exceptions per
op on Plaintext CL=256!); POST uploads are not retryable → the whole benchmark case errored.

## Root cause

`Http11ServerStateMachine.EmitBufferedBody` — the standard path for **buffered** response
bodies (i.e. virtually every normal MapGet/MapPost response) — called
`_responseRate.Observe(...)` per chunk but never `_responseRate.Remove(0)` on completion.
Only the *streaming* response path removed the entry. The stale entry's measured rate
decayed toward 0 B/s; once the connection sat idle on keep-alive longer than
`MinResponseDataRateGracePeriod` (default 5 s), the periodic `data-rate-check` timer flagged
a "violation" and set `ShouldComplete` → the server reset a perfectly healthy connection.

Trace signature: `data rate violation (reqRate=0, respRate=1, paused=False)` at Warning level.

Same leak in `Http10ServerStateMachine.HandleResponseBodyRead` (streaming completion,
`bytesRead == 0` branch) — relevant for `Connection: keep-alive` H1.0 clients.
H2/H3 are unaffected (per-stream entries removed in `CloseStream`).

## Why it was intermittent

Under continuous tight-loop load the Observe calls keep refreshing the rate, so no violation.
BDN's pauses between iterations (and HttpClient's >64 pooled connections rotating in and out
of use) created exactly the idle-past-grace windows. A 1000-round tight-loop repro stayed
green while the same scenario under BDN failed — first-chance exception counting +
Senf Warning tracing exposed it.

## The fix

- `EmitBufferedBody`: `_responseRate.Remove(0)` after `writer.CompleteAsync()`.
- `Http10ServerStateMachine`: same removal in the streaming-completion branch.

## Tests

- `Http11DataRateSpec.Buffered_response_completion_should_not_flag_idle_keepalive_connection`
  (FakeTimeProvider, buffered body via `TurboHttpResponseBodyFeature.Writer`, idle 10 s, two
  data-rate-check fires → must NOT set ShouldComplete; failed pre-fix).
- `Http10DataRateSpec.Completed_streaming_response_should_not_flag_idle_connection`.

## Verification

- Repro (HttpClient → TurboServer, 500×64 concurrent 1 MB uploads): pre-fix 13 connection
  resets + violation traces; post-fix 0/0 across 32 000 uploads.
- BDN `Upload_Concurrent` H1.1 CL=64/256: was NA (errored), post-fix produces results.

## Lesson

Rate-monitor entries are per-response state with mandatory removal on every completion path —
buffered, streamed, and failed. When BDN shows a 0/NA row, check the per-case log for
first-chance exceptions; HttpClient retry semantics can hide server connection-kills on GETs
while only POST benchmarks fail.
