## 1. H3 Frame Types — Remove IMemoryOwner constructors + IDisposable

- [x] 1.1 Remove `DataFrame(IMemoryOwner<byte> owner, int length)` constructor + `_owner` field + `Dispose()` from `Http3Frame.cs`
- [x] 1.2 Remove `HeadersFrame(IMemoryOwner<byte> owner, int length)` constructor + `_owner` field + `Dispose()`
- [x] 1.3 Remove `PushPromiseFrame(long pushId, IMemoryOwner<byte> owner, int length)` constructor + `_owner` field + `Dispose()`
- [x] 1.4 Update 3 test callers to use `ReadOnlyMemory<byte>` constructor instead + remove 7 `.Dispose()` calls on frames in Security tests + ZeroCopy test
- [x] 1.5 Removed unused `using System.Buffers` from Http3Frame.cs

## 2. BodySink Dead Code Removal

- [x] 2.1 Remove `BodySink` property + `_bodySink` field from `GaudiHttpResponseBodyFeature.cs` + stale BodySink reference in comment
- [x] 2.2 Remove 2 test methods that use BodySink from `GaudiHttpResponseBodyFeatureSpec.cs`

## 3. Stale Comment Fix

- [x] 3.1 Update timer key comment in H2 `StreamState.OnReset()` to reference TimerKeyCache

## 4. Verification

- [x] 4.1 Run `dotnet run --project GaudiHTTP.Tests/GaudiHTTP.Tests.csproj` — 6017/0 (-2 removed BodySink tests)
- [x] 4.2 Run `dotnet run --project GaudiHTTP.IntegrationTests.End2End/GaudiHTTP.IntegrationTests.End2End.csproj` — 104/0
