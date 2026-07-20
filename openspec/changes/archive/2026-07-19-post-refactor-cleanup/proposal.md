## Why

Cleanup pass after h3-zero-alloc + unified-decoder-pattern + hot-path-micro-alloc changes. Dead code paths, stale comments, and unused constructors left behind by the decoder refactor.

## What Changes

- Remove `IMemoryOwner<byte>` constructors from H3 `DataFrame`, `HeadersFrame`, `PushPromiseFrame` (3 test callers to update)
- Remove `IDisposable` implementation and `_owner` fields from same frame types (no longer needed — payloads are Memory slices, not owned)
- Remove dead `BodySink` property from `GaudiHttpResponseBodyFeature` (test-only, unused in production) + associated test methods
- Fix stale timer key comment in H2 `StreamState.OnReset()`

## Capabilities

### New Capabilities

### Modified Capabilities

## Impact

- `src/GaudiHTTP/Protocol/Syntax/Http3/Http3Frame.cs` — remove IMemoryOwner constructors + IDisposable
- `src/GaudiHTTP/Server/Context/Features/GaudiHttpResponseBodyFeature.cs` — remove BodySink property + _bodySink field
- `src/GaudiHTTP/Protocol/Syntax/Http2/StreamState.cs` — update comment
- `src/GaudiHTTP.Tests/` — update 3 test files using IMemoryOwner constructors, remove 2 BodySink tests
- No public API impact, no behavioral change
