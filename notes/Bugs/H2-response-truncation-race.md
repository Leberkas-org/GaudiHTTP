---
status: open
component: Protocol/Syntax/Http2/Server
discovered: '2026-06-11'
branch: fix/stress-benchmarks
severity: high
tags:
  - bug
  - http2
  - flow-control
  - race-condition
---
# H2 Response Truncation/Corruption Race (Concurrent Multiplexed Streams)

## Summary

Under concurrent multiplexed streams on a single HTTP/2 connection, large response bodies are intermittently **truncated by exact multiples of 16384 bytes (MAX_FRAME_SIZE)** or — less often — **delivered with the correct length but corrupted content**. The truncated response is surfaced to the caller as a **successful HTTP 200** with no exception.

This was previously misattributed to CPU starvation on CI runners. It is a real data-path race: it reproduces in full isolation on an idle 24-core machine (~1 in 5–10 runs of the repro class), and background CPU load only increases the firing rate.

## Observed failure modes

All evidence captured 2026-06-11 via the `X-Received-Length` diagnostic header in `ConcurrentLargePostSpec` (the echo endpoint reports how many request bytes the server actually received):

| Mode | Example | Interpretation |
|------|---------|----------------|
| Truncation, −1 frame | expected 1048576, got 1032192, `server received 1048576` | One 16 KB DATA frame lost on the response path |
| Truncation, −N frames | expected 1048576, got 835584 (−13 frames), `server received 1048576` | Multiple tail frames lost |
| Corruption | correct length, `SequenceEqual` fails | Frame content scrambled (possible buffer reuse race) |

Key facts:

- `server received 1048576` in **every** capture → the client→server request path is intact; the bug is on the **server→client response path** (or the client's response-body assembly).
- Truncation deltas are always exact multiples of 16384 (the negotiated MAX_FRAME_SIZE) — whole DATA frames disappear, never partial ones.
- The client completes the response **successfully**: no `HttpRequestException`, no Content-Length mismatch error. (Secondary bug: a truncated H2 response body should not surface as success.)
- The trigger is the *internal* concurrency of one connection (20 streams × 512 KB–1 MB). xUnit parallelization settings are irrelevant — it fires with fully serialized test execution.

## Reproduction

From `src/` with the Kestrel backend (PowerShell):

```powershell
$env:TURBOHTTP_TEST_BACKEND = "kestrel"
dotnet build --configuration Release TurboHTTP.slnx

# Loop until failure — typically fires within ~10 iterations on an idle machine,
# within ~5 under background CPU load:
foreach ($i in 1..30) {
  dotnet run --no-build --configuration Release `
    --project TurboHTTP.IntegrationTests.End2End/TurboHTTP.IntegrationTests.End2End.csproj `
    -- -class "TurboHTTP.IntegrationTests.End2End.H2.ConcurrentLargePostSpec" > "$env:TEMP\clp.txt"
  $t = (Select-String "$env:TEMP\clp.txt" -Pattern 'Total:').Line
  Write-Host "iter ${i}: $t"
  if ($t -match 'Failed: [1-9]') { break }
}
```

Failure messages include the diagnostic, e.g.:

```
Stream 3: expected 1048576 bytes, got 1032192 (server received 1048576)
```

`DefaultSettingsSmokeSpec.Defaults_should_handle_concurrent_POST_echo_without_rate_violations` (10 × 512 KB) fails the same way at a lower rate (`Payload mismatch (got 507904 bytes)` = 524288 − 16384).

To raise the firing rate, run the other integration suites concurrently as load generators, or run several copies of the loop in parallel.

## Suspect areas (not yet root-caused)

1. **Server response body drain** — `Http2ServerSessionManager.SendBufferedBodyWithFlowControl` + `DrainOutboundBuffer` + `HandleStreamBodyRead` (`src/TurboHttp/Protocol/Syntax/Http2/Server/Http2ServerSessionManager.cs`). This code was last touched by commit `17f990fb` ("fix(http): Improve flow control and stream draining") and handles exactly the window-exhausted chunk-queue path that concurrent streams exercise.
2. **Outbound transport queue / `TransportBuffer` reuse** — a queued buffer being re-rented and overwritten before the socket writes it would explain the corruption mode; both modes appearing together points toward the shared emit path (`EmitFrame` → `TransportBuffer.Rent` → `_ops.OnOutbound`).
3. **Client response-body assembly** — `Http2ClientSessionManager.HandleData` → `state.FeedBody(...)`; a chunk dropped between the connection actor and the user-thread body reader would also truncate. (Less likely to explain corruption.)

Debugging lever: Senf tracing per CLAUDE.md — `TraceLevel.Trace`, category `Protocol` shows every response body chunk, pause/resume, and the END_STREAM emission, on both client and server state machines.

## Secondary issue

The client returns a truncated response body as success. Even with the race fixed, the client should detect `received < Content-Length` (or END_STREAM before the declared length) and fail the request. Worth a dedicated unit test against `Http2ClientSessionManager`/response decoder.

## History / context

- Memory note `ci-flakiness-cpu-starvation.md` (project memory) documents the original misdiagnosis and the 2026-06-11 revision.
- The test-infrastructure flakiness that masked this bug (port races, unbounded parallelism, per-test cert generation, timeout collisions) was fixed on `fix/stress-benchmarks` on 2026-06-11; since then this race is the **only** remaining intermittent failure across all three integration suites.
