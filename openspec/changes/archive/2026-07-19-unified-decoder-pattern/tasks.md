## 0. Baseline

- [x] 0.1 Run `dotnet run --project GaudiHTTP.Tests/GaudiHTTP.Tests.csproj` — (Baseline: 6018/0, post h3-zero-alloc)
- [x] 0.2 Run `dotnet run --project GaudiHTTP.IntegrationTests.End2End/GaudiHTTP.IntegrationTests.End2End.csproj` — (Baseline: 104/0)

## 1. H2 FrameDecoder — Rewrite to caller-ownership pattern

- [x] 1.1 Rewrite `FrameDecoder` to match H3 pattern: `DecodeAll(ReadOnlyMemory<byte>, out int)`, field-based `byte[]` remainder, two-path decode (DecodeFromInput / DecodeWithRemainder), deferred compaction
- [x] 1.2 Remove `_workingBuffer` field, WireBuffer adoption logic, offset-wrapped fallback
- [x] 1.3 Preserve `maxFrameSize` validation, CONTINUATION state tracking, `_frames` reuse, `Reset()`, `Dispose()`
- [x] 1.4 Build main project — verify compilation

## 2. Production callers — caller-owned buffer

- [x] 2.1 Update `Http2ClientSessionManager.DecodeFrames` — pass `buffer.Memory`, no dispose (caller owns)
- [x] 2.2 Update `Http2ClientStateMachine.OnInbound` — `using var inputBuffer = buffer` around decode+process (disposes after frame consumption)
- [x] 2.3 Update `Http2ServerSessionManager.DecodeClientData` — `using var inputBuffer = buffer`, refactored SkipConnectionPreface to pure ReadOnlyMemory slicing
- [x] 2.4 Build main project — verify compilation (0 errors)

## 3. Test migration — Frames/ (direct FrameDecoder tests)

- [x] 3.1 Migrate all test files in `Frames/` folder (15 files): `Decode(WireBuffer)` → `DecodeAll(memory, out _)`, including DecoderReuseSpec rework and AcceptanceTests

## 4. Test migration — Client/ tests

- [x] 4.1 Migrate all test files in `Client/` folders (~18 files) + `Stages/Http2ConnectionTestHelper.cs` + `EngineTestBase.cs`

## 5. Test migration — Server/ + Security/ tests

- [x] 5.1 Migrate all test files in `Server/` folder (~4 files with FrameDecoder usage)
- [x] 5.2 Migrate all test files in `Security/` folder (~5 files)

## 6. Verification

- [x] 6.1 Run `dotnet run --project GaudiHTTP.Tests/GaudiHTTP.Tests.csproj` — 6019/0 (matches original baseline)
- [x] 6.2 Run `dotnet run --project GaudiHTTP.IntegrationTests.End2End/GaudiHTTP.IntegrationTests.End2End.csproj` — 104/0 (matches baseline)
- [x] 6.3 Grep: zero `Decode(` calls remain in H2 FrameDecoder — confirmed: only `DecodeAll` exists
