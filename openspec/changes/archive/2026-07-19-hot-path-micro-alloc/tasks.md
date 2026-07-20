## 0. Baseline

- [x] 0.1 Run `dotnet run --project GaudiHTTP.Tests/GaudiHTTP.Tests.csproj` — (Baseline: 6019/0, 18.3s)
- [x] 0.2 Run `dotnet run --project GaudiHTTP.IntegrationTests.End2End/GaudiHTTP.IntegrationTests.End2End.csproj` — (Baseline: 104/0)

## 1. stackalloc Small Buffers — INVESTIGATED: NO OPPORTUNITY

All H2/H3 encoders already write directly into Span via SpanWriter — no intermediate buffer allocation exists. H2 frame headers written via `Http2Frame.WriteHeader(ref SpanWriter)` directly into WireBuffer spans. H3 varints via `QuicVarInt.Encode(long, Span<byte>)`. HPACK/QPACK integer encoding via `WriteInteger(ref Span<byte>)`. Nothing to stackalloc.

- [x] 1.1–1.10 SKIPPED: No allocation to optimize — encoders are already zero-alloc on the hot path

## 2. Timer-Key Optimization

- [x] 2.1 Study current `StreamState.SetTimerKeys()` — 3 string allocs per stream (ToString + 2× Concat)
- [x] 2.2 Study timer API — Akka uses `object` key, stage logic uses `string`, dictionary equality lookup
- [x] 2.3 Design: static `Dictionary<int/long, (string, string)>` cache — stream IDs are monotonic, same ID always maps to same strings
- [x] 2.4 Implement `TimerKeyCache` in H2 `StreamState` — `GetOrCreate(int)` returns cached tuple
- [x] 2.5 Implement `TimerKeyCache` in H3 `StreamState` — `GetOrCreate(long)` returns cached tuple
- [x] 2.6 Timer scheduling/cancellation unchanged — still uses string keys, just cached
- [x] 2.7 OnReset() unchanged — timer key fields survive pool reuse (existing intentional behavior)
- [x] 2.8 Run unit tests — 6019/0, no regressions

## 3. SerialBodyPump Buffer-Reuse — INVESTIGATED: NOT VIABLE

`EmitOwnedDataFrames` takes ownership of the WireBuffer (transfers to transport). The pump cannot reclaim it after the call. The only per-read alloc is the ~40B WireBuffer wrapper (the array itself is pooled via SharedPool). The wrapper is deliberately not pooled (measured: break-even-or-worse vs Gen0 bump allocation). FlowControlled/Multiplexed pumps use `PumpSlot.EnsureBuffer` because they emit frame slices (not ownership transfer) — different contract.

- [x] 3.1–3.9 SKIPPED: Buffer ownership transfer prevents reuse

## 4. Server Body-Read — SelectAsync→Select (test-only code path)

`BodySink` is only used in tests, not in production (production uses PipeWriter directly). `CommitHeaders()` is synchronous (sets a flag + signals TCS). `SelectAsync` with `Task.FromResult` was unnecessary async wrapping.

- [x] 4.1 Identified `Task.FromResult` site in `GaudiHttpResponseBodyFeature.cs:166`
- [x] 4.2 Confirmed: `BodySink` is internal, test-only code path — not part of any interface
- [x] 4.3 No interface change needed
- [x] 4.4 Replaced `SelectAsync(1, chunk => Task.FromResult(chunk))` with `Select(chunk => { ... return chunk; })`
- [x] 4.5 Verified: only 2 test callers, both consume synchronously
- [x] 4.6 Run unit tests — 6019/0, no regressions

## 5. Verification

- [x] 5.1 Run `dotnet run --project GaudiHTTP.Tests/GaudiHTTP.Tests.csproj` — 6019/0 (matches baseline)
- [x] 5.2 Run `dotnet run --project GaudiHTTP.IntegrationTests.End2End/GaudiHTTP.IntegrationTests.End2End.csproj` — 104/0 (matches baseline)
- [x] 5.3 Investigation results documented: stackalloc (no opportunity), SerialBodyPump (not viable), timer keys (cached), BodySink (simplified)
