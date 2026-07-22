# Test Pipe Transport

Test infrastructure for delivering a real `IConnectionTransport` to state machines and stage tests, replacing `TransportData` port-based data flow with pipe-based I/O.

## Requirements

### Requirement: TestPipeTransport implements IConnectionTransport backed by real Pipes
`TestPipeTransport` MUST implement `IConnectionTransport` using two `System.IO.Pipelines.Pipe` instances: one for SM-reads (input) and one for SM-writes (output). It MUST live in `Servus.Akka.TestKit`.

#### Scenario: SM reads data fed by the test
- **WHEN** the test calls `transport.FeedInput(byte[] data)`
- **AND** the SM calls `transport.ReadAsync()`
- **THEN** the `ReadResult.Buffer` MUST contain the fed bytes
- **AND** `AdvanceTo(consumed, examined)` MUST correctly advance the PipeReader position

#### Scenario: SM writes data readable by the test
- **WHEN** the SM calls `transport.GetMemory(sizeHint)` and writes bytes, then `Advance(count)` and `FlushAsync()`
- **THEN** `transport.ReadOutputAsync()` MUST return the written bytes
- **AND** `transport.TryReadOutput(out ReadOnlySequence<byte> data)` MUST return `true` with the bytes when data is available

#### Scenario: CompleteInput signals end-of-stream to the SM
- **WHEN** the test calls `transport.CompleteInput()`
- **THEN** the next `ReadAsync()` MUST return `ReadResult.IsCompleted = true`

#### Scenario: Multiple feeds are concatenated in the pipe
- **WHEN** the test calls `FeedInput(data1)` then `FeedInput(data2)` without the SM reading
- **THEN** a single `ReadAsync()` MUST return a buffer containing both `data1` and `data2`

#### Scenario: Partial consume retains unconsumed bytes
- **WHEN** the SM reads a buffer and calls `AdvanceTo(consumed)` with a position partway through
- **THEN** the next `ReadAsync()` MUST return a buffer starting from the unconsumed position

---

### Requirement: TestConnectionStage exposes TestPipeTransport
`TestConnectionStage` MUST expose the `TestPipeTransport` created during connection setup so tests can feed data and read output.

#### Scenario: AutoConnectWithTransport creates transport on ConnectTransport
- **WHEN** `TestConnectionStageBuilder.AutoConnectWithTransport()` is used
- **AND** the SM sends a `ConnectTransport` outbound item
- **THEN** the stage MUST create a `TestPipeTransport` and push `TransportConnected(info, transport)`
- **AND** `stage.Transport` MUST return the created `TestPipeTransport`

#### Scenario: Transport property is null before connection
- **WHEN** no `ConnectTransport` has been received
- **THEN** `stage.Transport` MUST be `null`
