TASK-007: Validation gate — zero obsolete warnings, all tests green

Fix 7 pre-existing test failures in RFC9113 tests by adding CONTINUATION
state tracking and stream-0 validation to Http2FrameDecoder:

- Add DATA frame stream-0 rejection (RFC 9113 §6.1)
- Add CONTINUATION frame stream-0 rejection (RFC 9113 §6.10)
- Add CONTINUATION ordering enforcement: decoder now tracks whether it is
  awaiting a CONTINUATION frame after HEADERS/PUSH_PROMISE without
  END_HEADERS, rejecting interleaved or orphan frames per RFC 9113 §6.10
- Update 17 tests across 4 files to align with the new decoder-level
  validation (previously expected validation at session layer)
- Fix pseudo-header index assertion in 20_EncoderPseudoHeaderTests.cs
- Fix oversized DATA frame test to document stateless MAX_FRAME_SIZE

Validation results:
- dotnet build: 0 warnings, 0 errors
- dotnet test: 2451 passed (1818 unit + 633 stream), 0 failed
- grep CS0618: no pragma suppressions in src/
- grep [Obsolete]: no obsolete attributes in production code
