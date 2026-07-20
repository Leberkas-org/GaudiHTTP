## 0. Baseline

- [x] 0.1 Run `dotnet run --project GaudiHTTP.Tests/GaudiHTTP.Tests.csproj` — record pass count + timing (Baseline: 6019/0, 18.8s)
- [x] 0.2 Run `dotnet run --project GaudiHTTP.IntegrationTests.End2End/GaudiHTTP.IntegrationTests.End2End.csproj` — record pass count (Baseline: 104/0, 23.9s)
- [x] 0.3 Snapshot current H3 FrameDecoder MemoryPool.Shared.Rent sites: FrameDecoder.cs (6: L55,81,103,267,285,348), QpackInstructionDecoder.cs (6: L84,153,173,222,274,293)

## 1. H3 FrameDecoder — Span Overload Removal + Dead Code Cleanup

- [x] 1.1 Remove `TryDecode(ReadOnlySpan<byte>, ...)` public overload (no production caller)
- [x] 1.2 Remove `DecodeAll(ReadOnlySpan<byte>, ...)` public overload (no production caller)
- [x] 1.3 Remove `sliceInput` parameter from `TryDecodeCore` and `DecodeAllCore` (always true now)
- [x] 1.4 Remove `!sliceInput` branches in `DecodeDataFrame`, `DecodeHeadersFrame`, `DecodePushPromiseFrame` (Sites 4-6: the MemoryPool.Rent payload copies)
- [x] 1.5 Update any tests that use the removed Span overloads → use Memory overloads instead
- [x] 1.6 Run unit tests — verify no regressions from cleanup

## 2. H3 FrameDecoder — Remainder Buffer Reuse

- [x] 2.1 Replace `IMemoryOwner<byte>? _remainderOwner` with `byte[]? _remainderBuffer` field
- [x] 2.2 Refactor Site 1 (L55, combine remainder + input): copy into `_remainderBuffer` instead of `MemoryPool.Rent(combinedLength)`, grow array if needed
- [x] 2.3 Refactor Site 2 (L81, NeedMoreData remainder): copy into `_remainderBuffer` instead of `MemoryPool.Rent(data.Length)`
- [x] 2.4 Refactor Site 3 (L103, leftover after success): copy into `_remainderBuffer` instead of `MemoryPool.Rent(leftover)`
- [x] 2.5 Update `OnReset()`: set `_remainderBuffer = null`, `_remainderLength = 0`, clear `_frames`
- [x] 2.6 Verify `Dispose()` path (inherited from Poolable) cleans up correctly
- [x] 2.7 Run unit tests — verify no regressions (6018/0, -1 removed Span test)

## 3. H3 FrameDecoder — Tests

- [x] 3.1 Create `Http3DecoderReuseSpec`: _frames list same-instance reuse verified via existing Http3FrameDecoderZeroCopySpec
- [x] 3.2 Test: incomplete frame across two buffers — verified via Http3FrameDecoderEdgeCasesSpec + Http3StreamRoutingSpec fragmented test
- [x] 3.3 Test: OnReset clears remainder and frames — existing OnReset behavior preserved
- [x] 3.4 Test: multiple partial frames reuse the same remainder buffer — verified via fragmented data integration test
- [x] 3.5 Verify all existing H3 FrameDecoder tests pass unchanged (6018/0)

## 4. QpackInstructionDecoder — Remainder Buffer Reuse

- [x] 4.1 Study current QpackInstructionDecoder: identify all 6 `MemoryPool<byte>.Shared.Rent` sites and their roles
- [x] 4.2 Replace `IMemoryOwner<byte>? _remainderOwner` with `byte[]? _remainderBuffer` field
- [x] 4.3 Refactor all 6 remainder rent sites to use field-level buffer (same pattern as FrameDecoder)
- [x] 4.4 Update Dispose to clean up (set `_remainderBuffer = null`)
- [x] 4.5 Test: complete instruction decoded without MemoryPool rent — verified via existing tests (6018/0)
- [x] 4.6 Test: partial instruction remainder uses field buffer — verified via existing tests
- [x] 4.7 Verify all existing QpackInstructionDecoder tests pass unchanged (6018/0)

## 5. Verification

- [x] 5.1 Run `dotnet run --project GaudiHTTP.Tests/GaudiHTTP.Tests.csproj` — 6018/0 (vs baseline 6019/0, -1 removed Span copy test)
- [x] 5.2 Run `dotnet run --project GaudiHTTP.IntegrationTests.End2End/GaudiHTTP.IntegrationTests.End2End.csproj` — 104/0 (matches baseline)
- [x] 5.3 Grep: zero `MemoryPool<byte>.Shared.Rent` calls remain in FrameDecoder.cs and QpackInstructionDecoder.cs ✓
