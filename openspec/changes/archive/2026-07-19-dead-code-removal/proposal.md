## Why

Post-refactor dead code analysis (Roslyn + grep, 3 parallel agents) identified 4 entire dead classes, 7 dead production methods, and several test-only methods in production assemblies. These add maintenance burden, confuse navigation, and inflate the binary.

## What Changes

**Remove 4 dead classes:**
- `StreamTracker` (H3) — replaced by `QuicStreamTracker`, only test+doc references remain
- `ChunkExtensionParser` (H11) — only test callers, production chunked decoding doesn't use it
- `ConnectionReuseEvaluator` (H11) — only test callers, connection reuse handled elsewhere
- `ConnectionReuseDecision` (H11) — only referenced by dead `ConnectionReuseEvaluator`

**Remove 3 dead production methods:**
- `GetResponsePipeReader()` in `GaudiHttpResponseBodyFeature` — zero callers
- `ReturnBuffer()` in `FeatureCollectionFactory` — zero callers (buffers never returned)
- `StreamManager.FailInflightRequest()` (H3) — zero callers

**Remove dead H3 ConnectionState members:**
- `RecordPush()`, `IsPushCancelled()`, `MaxPushId`, `ComputeEffectiveTimeout()` — test-only, unused push infrastructure

**Remove dead H3 constants:**
- `CriticalStreamId.PushId`, `CriticalStreamId.Push` — zero references

**Fix stale references:**
- Remove `BodySink` comment in `Http11ServerEncoder.cs:78`
- Rename `ClientCorrelationKeys.cs` → `OptionsKey.cs`

## Capabilities

### New Capabilities

### Modified Capabilities

## Impact

- 4 `.cs` production files deleted entirely
- ~8 methods/properties removed from existing files
- Associated test files for dead classes removed
- File rename: `Internal/ClientCorrelationKeys.cs` → `Internal/OptionsKey.cs`
- No behavioral change, no public API impact
