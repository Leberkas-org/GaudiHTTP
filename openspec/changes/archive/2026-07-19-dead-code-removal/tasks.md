## 1. Delete Dead Classes

- [x] 1.1 Delete `src/GaudiHTTP/Protocol/Syntax/Http3/StreamTracker.cs`
- [x] 1.2 Delete associated test `Http3StreamTrackerSpec.cs`
- [x] 1.3 Fix `<see cref="StreamTracker"/>` → `QuicStreamTracker` in `Client/StreamManager.cs:573`
- [x] 1.4 Delete `src/GaudiHTTP/Protocol/Syntax/Http11/ChunkExtensionParser.cs`
- [x] 1.5 Delete associated test `ChunkExtensionParserSpec.cs`
- [x] 1.6 Delete `src/GaudiHTTP/Protocol/Syntax/Http11/ConnectionReuseEvaluator.cs`
- [x] 1.7 Delete `src/GaudiHTTP/Protocol/Syntax/Http11/ConnectionReuseDecision.cs`
- [x] 1.8 Delete associated test `ConnectionReuseEvaluatorSpec.cs`
- [x] 1.9 Build — zero errors

## 2. Remove Dead Methods

- [x] 2.1 Remove `GetResponsePipeReader()` from `GaudiHttpResponseBodyFeature.cs`
- [x] 2.2 Remove `ReturnBuffer()` from `FeatureCollectionFactory.cs`
- [x] 2.3 Remove `FailInflightRequest()` from `Protocol/Syntax/Http3/Client/StreamManager.cs`
- [x] 2.4 Build — zero errors

## 3. Remove Dead H3 ConnectionState Members

- [x] 3.1 Remove `RecordPush()`, `IsPushCancelled()`, `MaxPushId`, `ComputeEffectiveTimeout()`, `_cancelledPushIds`, `_pushCount` from `ConnectionState.cs`
- [x] 3.2 Remove `CriticalStreamId.PushId` and `CriticalStreamId.Push`
- [x] 3.3 Delete `Http3PushStreamSpec.cs` (entire file tested only removed members) + remove 15 test methods from `Http3ConnectionStateEdgeCasesSpec.cs`
- [x] 3.4 Build — zero errors

## 4. Stale References + Naming

- [x] 4.1 Remove `BodySink` comment in `Http11ServerEncoder.cs:78`
- [x] 4.2 Rename `Internal/ClientCorrelationKeys.cs` → `Internal/OptionsKey.cs` (git mv)
- [x] 4.3 Build — zero errors

## 5. Verification

- [x] 5.1 Unit tests: 5962/0 (−55 removed dead-code tests from 6017 baseline)
- [x] 5.2 E2E integration: 104/0 (matches baseline)
