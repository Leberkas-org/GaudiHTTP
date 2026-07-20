## ADDED Requirements

### Requirement: SerialBodyPump reuses buffer across reads for chunked and content-length bodies
SerialBodyPump SHALL rent a WireBuffer once per body stream and reuse it across consecutive `ReadAsync` calls, instead of renting a fresh WireBuffer per read. The buffer SHALL be disposed when the body stream completes or on pump cleanup.

#### Scenario: Chunked body upload reuses single buffer
- **WHEN** a chunked H1.1 request body is uploaded with 100 chunks
- **THEN** SerialBodyPump SHALL rent at most 1 WireBuffer (not 100)
- **THEN** the buffer SHALL be reused for each ReadAsync call

#### Scenario: Content-Length body upload reuses single buffer
- **WHEN** a Content-Length H1.1 request body is uploaded in multiple reads
- **THEN** SerialBodyPump SHALL reuse the same WireBuffer across reads

#### Scenario: Buffer disposed on body completion
- **WHEN** the body stream completes (EOF or error)
- **THEN** the reused buffer SHALL be disposed

#### Scenario: Buffer disposed on pump cleanup
- **WHEN** the pump is cleaned up (connection close, cancel)
- **THEN** any held buffer SHALL be disposed without leak

### Requirement: Identity-encoded bodies preserve zero-copy ownership transfer
For H1.1 identity encoding, SerialBodyPump SHALL continue to rent a fresh WireBuffer per read and transfer ownership via `WireBuffer.Wrap()`. Buffer reuse MUST NOT apply to the identity path because the transport takes ownership of each buffer.

#### Scenario: Identity body upload transfers buffer ownership per chunk
- **WHEN** an identity-encoded H1.1 request body is uploaded
- **THEN** each read SHALL rent a fresh WireBuffer
- **THEN** each WireBuffer SHALL be transferred to the transport via Wrap (zero-copy)
- **THEN** the pump SHALL NOT retain a reference to the transferred buffer

### Requirement: Buffer grows if chunk size increases
If the body stream produces reads larger than the current buffer capacity, the buffer SHALL be replaced with a larger one (rent new, dispose old).

#### Scenario: Buffer replaced when read exceeds capacity
- **WHEN** the first read is 16 KB but a subsequent read needs 32 KB
- **THEN** the pump SHALL dispose the 16 KB buffer and rent a 32 KB buffer
