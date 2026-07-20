## REMOVED Requirements

### Requirement: StreamTracker (H3)
**Reason**: Replaced by `QuicStreamTracker`; zero production callers remain.
**Migration**: No migration needed — `QuicStreamTracker` already handles all H3 stream tracking.

#### Scenario: Production code compiles without StreamTracker
- **WHEN** `StreamTracker.cs` is deleted
- **THEN** the project SHALL build with zero compilation errors

### Requirement: ChunkExtensionParser (H11)
**Reason**: Never integrated into production chunked transfer decoding.
**Migration**: None — production chunked decoding in `Http11ClientDecoder`/`Http11ServerDecoder` handles extensions inline.

#### Scenario: Production code compiles without ChunkExtensionParser
- **WHEN** `ChunkExtensionParser.cs` is deleted
- **THEN** the project SHALL build with zero compilation errors

### Requirement: ConnectionReuseEvaluator and ConnectionReuseDecision (H11)
**Reason**: Connection reuse logic handled directly by `Http11PoolingStrategy`; these types never called from production.
**Migration**: None.

#### Scenario: Production code compiles without ConnectionReuseEvaluator
- **WHEN** both `ConnectionReuseEvaluator.cs` and `ConnectionReuseDecision.cs` are deleted
- **THEN** the project SHALL build with zero compilation errors

### Requirement: Dead methods in GaudiHttpResponseBodyFeature and FeatureCollectionFactory
**Reason**: `GetResponsePipeReader` has zero callers (alternatives used instead). `ReturnBuffer` has zero callers (buffer pool return path never wired).
**Migration**: None.

#### Scenario: Production code compiles without dead methods
- **WHEN** `GetResponsePipeReader()` and `ReturnBuffer()` are removed
- **THEN** the project SHALL build with zero compilation errors

### Requirement: Dead H3 push infrastructure in ConnectionState
**Reason**: Push support rejected at protocol level via `CancelPushFrame`; tracking methods never called.
**Migration**: None. If push support is needed in future, it will be redesigned.

#### Scenario: Production code compiles without push tracking
- **WHEN** `RecordPush`, `IsPushCancelled`, `MaxPushId`, `ComputeEffectiveTimeout` are removed from `ConnectionState`
- **THEN** the project SHALL build with zero compilation errors
