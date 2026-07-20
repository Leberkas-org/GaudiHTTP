## REMOVED Requirements

### Requirement: IMemoryOwner constructors on H3 frame types
**Reason**: No production callers after FrameDecoder refactor removed the `!sliceInput` path.
**Migration**: Tests use `ReadOnlyMemory<byte>` constructor instead.

#### Scenario: Frames no longer implement IDisposable
- **WHEN** `DataFrame`, `HeadersFrame`, `PushPromiseFrame` no longer implement `IDisposable`
- **THEN** all production and test code SHALL compile without `.Dispose()` calls on frame instances

### Requirement: BodySink property on GaudiHttpResponseBodyFeature
**Reason**: Test-only dead code; production uses PipeWriter directly.
**Migration**: 2 test methods removed.

#### Scenario: Feature compiles without BodySink
- **WHEN** the `BodySink` property is removed
- **THEN** the project SHALL build with zero compilation errors
