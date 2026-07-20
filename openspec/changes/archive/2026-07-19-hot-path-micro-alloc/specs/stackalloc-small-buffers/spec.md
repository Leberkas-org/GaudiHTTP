## ADDED Requirements

### Requirement: H2 frame headers encoded via stackalloc
H2 Encoder SHALL use `stackalloc byte[9]` for the 9-byte DATA/HEADERS frame header instead of writing directly into the output buffer via offset arithmetic or renting a temporary buffer.

#### Scenario: DATA frame header uses stackalloc
- **WHEN** the H2 client or server encoder writes a DATA frame
- **THEN** the 9-byte frame header SHALL be constructed on the stack before copying into the output WireBuffer
- **THEN** no heap allocation SHALL occur for the header bytes

#### Scenario: HEADERS frame header uses stackalloc
- **WHEN** the H2 encoder writes a HEADERS/CONTINUATION frame
- **THEN** the frame header SHALL use stackalloc

### Requirement: H3 varints encoded via stackalloc
H3 Encoder SHALL use `stackalloc byte[8]` for QUIC variable-length integer encoding (max 8 bytes per varint).

#### Scenario: Frame type + length varint uses stackalloc
- **WHEN** the H3 encoder writes a frame type and frame length
- **THEN** the varint encoding SHALL use stack-allocated memory
- **THEN** no heap allocation SHALL occur for the varint bytes

### Requirement: HPACK/QPACK integer encoding via stackalloc
HPACK and QPACK integer encoding SHALL use `stackalloc byte[10]` (max encoded integer size) for the prefix-encoded integer before writing into the output buffer.

#### Scenario: HPACK integer encoding uses stackalloc
- **WHEN** HpackEncoder encodes a header index or string length
- **THEN** the integer encoding scratch space SHALL be stack-allocated

#### Scenario: QPACK integer encoding uses stackalloc
- **WHEN** QpackEncoder encodes an instruction integer
- **THEN** the integer encoding scratch space SHALL be stack-allocated

### Requirement: stackalloc limited to 64 bytes maximum
No single stackalloc in encoder/decoder hot paths SHALL exceed 64 bytes. All stackalloc sites MUST be on synchronous Actor-Thread paths (no await between alloc and last use).

#### Scenario: No large stackalloc in encoders
- **WHEN** reviewing all stackalloc sites introduced by this change
- **THEN** every stackalloc SHALL be 64 bytes or fewer
- **THEN** every stackalloc SHALL be consumed synchronously (no async boundary)
