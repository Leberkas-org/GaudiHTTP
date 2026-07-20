## ADDED Requirements

### Requirement: Server body read returns ValueTask instead of Task
`GaudiHttpResponseBodyFeature` SHALL return `ValueTask<ReadResult>` (or equivalent ValueTask-based return) instead of `Task.FromResult()` for synchronous body chunk reads. This eliminates one Task object allocation per response body chunk.

#### Scenario: Synchronous body read produces zero Task allocation
- **WHEN** response body data is already available (synchronous path)
- **THEN** the feature SHALL return `new ValueTask<T>(result)` (zero-alloc)
- **THEN** no `Task.FromResult()` SHALL be called

#### Scenario: Asynchronous body read still works
- **WHEN** response body data is not yet available (async path)
- **THEN** the feature SHALL return a ValueTask backed by an async operation
- **THEN** the caller SHALL receive the data when it becomes available

### Requirement: Internal interface updated to support ValueTask return
The internal interface consumed by the server body feature SHALL accept `ValueTask<T>` return types. This change MUST NOT affect any public API surface.

#### Scenario: Interface change is internal only
- **WHEN** reviewing the change for API impact
- **THEN** no public type signatures SHALL be modified
- **THEN** the change SHALL be limited to internal interface(s) and their implementations
