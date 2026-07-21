## 1. H2 FrameDecoder Migration

- [x] 1.1 Change `Http2/FrameDecoder.DecodeAll` signature to `(in ReadOnlySequence<byte> input, out SequencePosition consumed)`
- [x] 1.2 Remove `_remainderBuffer`, `_remainderOffset`, `_remainderLength` fields and `EnsureRemainderCapacity`/compaction logic
- [x] 1.3 Implement single-segment fast path operating on `input.FirstSpan` (reuse existing header-walk loop, adapt offset math)
- [x] 1.4 Implement multi-segment path using `SequenceReader<byte>` for header parsing (9-byte fixed header) and payload slicing
- [x] 1.5 Preserve maxFrameSize validation, `_frames` list reuse, and CONTINUATION state tracking unchanged across both paths
- [x] 1.6 Update `Reset()`/`Dispose()` — remove remainder-field resets, keep `_awaitingContinuationStreamId` reset

## 2. H3 FrameDecoder Migration

- [x] 2.1 Change `Http3/FrameDecoder.DecodeAll` signature to `(in ReadOnlySequence<byte> input, out SequencePosition consumed)`
- [x] 2.2 Remove remainder fields; implement single-segment fast path (existing `QuicVarInt.TryDecode`-based loop on `input.FirstSpan`)
- [x] 2.3 Implement multi-segment path using `SequenceReader<byte>` for varint type/length decode across segment boundaries
- [x] 2.4 Preserve unknown-frame-type skipping and `_frames` list reuse unchanged across both paths
- [x] 2.5 Update `OnReset()` (pooling contract) — remove remainder-field resets

## 3. SM Call-Site Updates (mechanical)

- [x] 3.1 Update H2 client call site (`Http2ClientSessionManager`): wrap `WireBuffer.Memory` in `new ReadOnlySequence<byte>(memory)`, adapt `out int` → `out SequencePosition` usage
- [x] 3.2 Update H2 server call site (`Http2ServerSessionManager`): same wrap
- [x] 3.3 Update H3 client call site (`Http3/Client/StreamManager`): same wrap
- [x] 3.4 Update H3 server call site (`Http3ServerSessionManager`): same wrap
- [x] 3.5 Update `Qpack`/`Hpack` or other non-decoder call sites only if they directly invoke `FrameDecoder.DecodeAll` (verify via caller search; QpackInstructionDecoder/QpackTableSync appear in the caller list — confirm whether they call `FrameDecoder.DecodeAll` or an unrelated `DecodeAll` and scope accordingly)
- [x] 3.6 Audit all call sites for any logic keyed on `bytesConsumed == input.Length` and confirm the equivalent `consumed == seq.End` check still holds

## 4. Existing Test Migration (mechanical, group by protocol)

- [x] 4.1 Update all H2 `Frames/` test files (`Http2DecoderBasicFrameSpec`, `Http2FrameDecoderBoundarySpec`, `Http2DecoderPaddingSpec`, `Http2ContinuationFrameAssemblySpec`, `Http2ContinuationFrameErrorSpec`, `Http2DecoderStreamStateSpec`, `Http2DecoderStreamValidationSpec`, `Http2DecoderPushPromiseSpec`, `Http2DecoderErrorCodeSpec`, `Http2DecoderUnknownErrorCodeSpec`, `Http2DataFrameFlowControlLengthSpec`, `Http2DecoderReuseSpec`, `Http2FrameDecoderStreamConstraintSpec`, `Http2EncoderStreamSettingsSpec`, `Http2ErrorHandlingSpec`) to wrap fixtures in `new ReadOnlySequence<byte>(memory)` and consume `SequencePosition`
- [x] 4.2 Update H2 `Client/`, `Server/`, `Security/`, `Stages/` test files that call `DecodeAll` directly (Settings, FlowControl, StateMachine, Streaming subfolders per the caller list)
- [x] 4.3 Update all H3 `Frames/` test files (`Http3FrameDecoderSpec`, `Http3FrameDecoderEdgeCasesSpec`, `Http3FrameDecoderMalformedSpec`, `Http3FrameRoundTripSpec`, `Http3ExtensionToleranceSpec`, `Http3FrameDecoderZeroCopySpec`)
- [x] 4.4 Update H3 `Client/`, `Server/`, `Security/` test files that call `DecodeAll` directly
- [x] 4.5 Update `GaudiHTTP.Tests.Shared/EngineTestBase.cs` and `GaudiHTTP.AcceptanceTests` response builder specs (`H2ResponseBuilderSpec`, `H3ResponseBuilderSpec`) if they construct `DecodeAll` calls directly

## 5. New Multi-Segment Tests

- [x] 5.1 H2: frame header (9 bytes) split across two segments — assert correct type/flags/streamId/length parsed
- [x] 5.2 H2: frame payload split across two segments — assert payload bytes match single-segment equivalent
- [x] 5.3 H2: multiple frames spanning several segment boundaries in one `DecodeAll` call
- [x] 5.4 H2: `consumed` correctness when a multi-segment input ends mid-frame (partial frame after N complete frames)
- [x] 5.5 H3: varint type/length field split across two segments — assert correct decode
- [x] 5.6 H3: frame payload split across two segments
- [x] 5.7 H3: unknown frame type spanning a segment boundary is still skipped correctly
- [x] 5.8 Both: zero-copy assertion for single-segment case still holds (no regression) — payload memory aliases input segment

## 6. Verification

- [x] 6.1 Build GaudiHTTP + GaudiHTTP.Tests, zero compile diagnostics
- [x] 6.2 Run full unit+stage suite (`dotnet run --project GaudiHTTP.Tests`), target all green
- [x] 6.3 Run H2/H3-relevant integration suites (`End2End`) to confirm no behavioral regression in the single-segment (common) path
- [x] 6.4 Confirm no leftover references to `_remainderBuffer`/`_remainder`/`bytesConsumed` in decoder or call-site code (grep sweep)
- [x] 6.5 Record the Phase B transitional risk (cross-call partial-frame gap, see design.md) as an open item to close when `pipe-transport-tcp` Phase A merges
