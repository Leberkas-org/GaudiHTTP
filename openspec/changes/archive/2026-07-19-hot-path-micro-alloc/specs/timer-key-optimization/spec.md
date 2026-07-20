## ADDED Requirements

### Requirement: Timer keys use integer-packed identifiers instead of string concatenation
H2 and H3 `StreamState.SetTimerKeys()` SHALL NOT call `string.Concat` or `ToString()` to build timer keys. Timer keys SHALL be integer-packed values that encode timer kind and stream ID without heap allocation.

#### Scenario: H2 stream open produces zero string allocations for timer keys
- **WHEN** an H2 stream is opened and `SetTimerKeys()` is called
- **THEN** no `string.Concat` or `int.ToString()` SHALL be called
- **THEN** timer keys SHALL be integer-packed values

#### Scenario: H3 stream open produces zero string allocations for timer keys
- **WHEN** an H3 stream is opened and `SetTimerKeys()` is called
- **THEN** no `string.Concat` or `long.ToString()` SHALL be called
- **THEN** timer keys SHALL be integer-packed values

### Requirement: Timer scheduling and cancellation work with new key format
The stage logic timer infrastructure SHALL accept the new integer-packed keys for scheduling, cancelling, and handling timer events.

#### Scenario: Timer fires with correct stream identification
- **WHEN** a body-consumption timer fires for stream ID 42
- **THEN** the stage logic SHALL correctly identify stream 42 from the packed key
- **THEN** the correct timeout action SHALL execute

#### Scenario: Timer cancellation targets correct stream
- **WHEN** a timer is cancelled for a specific stream
- **THEN** only that stream's timer SHALL be cancelled
- **THEN** other streams' timers SHALL remain active

### Requirement: Pooled StreamState resets timer keys on return
Since StreamState is pooled, `OnReset()` SHALL clear timer key fields to prevent stale key references.

#### Scenario: Recycled StreamState does not carry stale timer keys
- **WHEN** a StreamState is returned to the pool and re-rented for a new stream
- **THEN** timer keys SHALL reflect the new stream ID, not the previous one
