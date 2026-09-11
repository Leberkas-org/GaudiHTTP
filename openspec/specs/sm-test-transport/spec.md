# SM Test Transport

InMemoryTransport for unit-testing TCP state machines without Akka Streams infrastructure. Provides scripted read delivery, write capture, and configurable flush behavior via `IConnectionTransport`.

## Requirements

### Requirement: InMemoryTransport implements IConnectionTransport for unit tests
`InMemoryTransport` MUST implement `IConnectionTransport` and provide scripted read delivery,
write capture, and configurable flush behavior. It MUST live in `GaudiHTTP.Tests.Shared` and
serve as the standard way to unit-test TCP state machines without Akka Streams infrastructure.

#### Scenario: Feed delivers data via ReadAsync
- **WHEN** `Feed(byte[] data)` is called on an `InMemoryTransport`
- **AND** a `ReadAsync()` call is pending or subsequently made
- **THEN** the `ReadResult.Buffer` MUST contain the fed bytes as a `ReadOnlySequence<byte>`
- **AND** `IsCompleted` MUST be `false`

#### Scenario: Complete signals end-of-stream
- **WHEN** `Complete()` is called on an `InMemoryTransport`
- **AND** `ReadAsync()` is subsequently called
- **THEN** `ReadResult.IsCompleted` MUST be `true`
- **AND** `ReadResult.Buffer` MUST be empty

#### Scenario: AdvanceTo consumes read data
- **WHEN** `AdvanceTo(consumed, examined)` is called after a read
- **THEN** consumed bytes MUST be removed from the internal buffer
- **AND** examined bytes MUST be retained for the next read

#### Scenario: GetMemory returns writable memory
- **WHEN** `GetMemory(sizeHint)` is called
- **THEN** it MUST return a `Memory<byte>` with at least `sizeHint` bytes available
- **AND** multiple calls without `Advance` MUST return the same memory region

#### Scenario: Advance tracks written bytes
- **WHEN** `Advance(count)` is called after writing into `GetMemory`
- **THEN** `WrittenBytes` MUST include those bytes in order
- **AND** subsequent `GetMemory` calls MUST return memory after the advanced position

#### Scenario: WrittenBytes captures all outbound data
- **WHEN** the SM writes response/request data via `GetMemory`/`Advance`
- **THEN** `transport.WrittenBytes` MUST contain the complete encoded output
- **AND** `TakeWrittenBytes()` MUST return the bytes and reset the write position

#### Scenario: FlushAsync behavior is configurable
- **WHEN** `FlushMode` is set to `Sync`
- **THEN** `FlushAsync()` MUST return a completed `ValueTask<FlushResult>` with `IsCompleted = false`
- **WHEN** `FlushMode` is set to `Async`
- **THEN** `FlushAsync()` MUST return a pending `ValueTask<FlushResult>`
- **WHEN** `FlushMode` is set to `SyncCompleted`
- **THEN** `FlushAsync()` MUST return a completed `ValueTask<FlushResult>` with `IsCompleted = true`

---

### Requirement: InMemoryTransport supports lifecycle simulation
`InMemoryTransport` MUST be usable with `TcpStateMachineBase.DispatchLifecycleEvent` to simulate
the full transport lifecycle (connect -> data -> disconnect) in unit tests.

#### Scenario: Connect via DispatchLifecycleEvent
- **WHEN** `sm.DispatchLifecycleEvent(new TransportConnected(info, transport))` is called
- **THEN** the SM MUST store the transport and start the read loop
- **AND** subsequent `Feed()` calls on the transport MUST deliver data to the SM's `DecodeData`

#### Scenario: Disconnect via DispatchLifecycleEvent
- **WHEN** `sm.DispatchLifecycleEvent(new TransportDisconnected(reason))` is called
- **THEN** the SM MUST clear the transport and increment `_transportGen`
