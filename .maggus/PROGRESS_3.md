# Plan Progress — Test Audit & Restructuring

## TASK-ANA-001: Create the Audit Report
**Status:** COMPLETE | **Date:** 2026-03-12

**Deliverable:** `docs/test-audit-report.md`

**Findings Summary:**
- 21 test files (521 tests) in RFC9113/ use `Http2ProtocolSession`
- `Http2ProtocolSession` covers RFC 9113 §3.4, §4.3, §5.1, §6.2, §6.5, §6.7, §6.8, §6.9, §6.10, §8.2, §8.3, §8.3.2 + security protections
- 819 tests across all 3 projects are missing RFC references in DisplayName:
  - TurboHttp.Tests: 182 bare + 262 without RFC = 444
  - TurboHttp.StreamTests: 175 without RFC
  - TurboHttp.IntegrationTests: 200 without RFC
- Integration folder mapping: 10 files -> RFC6265/ (1), RFC9110/ (6), RFC9112/ (3)
- StreamTests folder mapping: Http10/ -> RFC1945/, Http11/ -> RFC9112/, Http20/ -> RFC9113/
- Build: 0 errors (1 pre-existing CS0169 warning)

---

## TASK-PSS-001: Replace Http2ProtocolSession — Stream State Tests
**Status:** COMPLETE | **Date:** 2026-03-17

**Changes:**
- Rewrote `src/TurboHttp.Tests/RFC9113/03_StreamStateMachineTests.cs`
- Removed `Http2ProtocolSession` and `Http2StreamLifecycleState` (test-only classes)
- Now uses only `Http2FrameDecoder`, `Http2Frame` subclasses, `HpackDecoder`, `HpackEncoder`
- Class renamed from `Http2StreamLifecycleTests` to `Http2StreamStateMachineTests` (matches filter `FullyQualifiedName~StreamStateMachine`)
- 25 old tests → 12 new tests; all DisplayNames contain `RFC-9113-§5.1`
- All 4 required scenarios covered: Idle→Open, Open→Closed, HEADERS on stream 0 → Exception, DATA on idle stream → Exception
- Test count: 2123 (pre-existing RH-015 failure unchanged)

---

## TASK-PSS-002: Replace Http2ProtocolSession — Settings Tests (RFC9113 §6.5)
**Status:** COMPLETE | **Date:** 2026-03-17

**Changes:**
- Rewrote `src/TurboHttp.Tests/RFC9113/04_SettingsTests.cs`
- Removed all `Http2ProtocolSession` references; old class `Http2SettingsSynchronizationTests` replaced with `Http2SettingsTests`
- Now uses only `SettingsFrame`, `Http2FrameDecoder`, `SettingsParameter`
- 29 old tests → 20 new tests; all DisplayNames contain `RFC-9113-§6.5`
- Scenarios covered: ACK flag (SS-001..002), stream-0 constraint (SS-003), FRAME_SIZE_ERROR (SS-004..005), MAX_FRAME_SIZE range (SS-006..009), ENABLE_PUSH validation (SS-010..013), INITIAL_WINDOW_SIZE overflow (SS-014..016), parameter parsing (SS-017..020)
- Validation helpers for ENABLE_PUSH and INITIAL_WINDOW_SIZE follow the caller-responsibility pattern from `03_StreamStateMachineTests.cs`
- All 35 tests matching `FullyQualifiedName~SettingsTests` pass

---

---

## TASK-PSS-003: Replace Http2ProtocolSession — Flow Control Tests (RFC9113 §6.9)
**Status:** COMPLETE | **Date:** 2026-03-17

**Changes:**
- Rewrote `src/TurboHttp.Tests/RFC9113/05_FlowControlTests.cs`
- Rewrote `src/TurboHttp.Tests/RFC9113/13_DecoderStreamFlowControlTests.cs`
- Both files now use only `WindowUpdateFrame`, `DataFrame`, `Http2FrameDecoder`
- No `Http2ProtocolSession` references remain in either file
- 38 + 4 old tests → 26 + 6 new tests; all DisplayNames contain `RFC-9113-§6.9`
- Scenarios covered: WINDOW_UPDATE stream 0 (FC-WU-001..006), stream N (FC-WU-007..012), increment edge cases (FC-WU-013..016), error cases — zero increment PROTOCOL_ERROR (FC-WU-017..018), wrong payload size FRAME_SIZE_ERROR (FC-WU-019), DATA frame decoding (FC-DF-001..007), decoder stream tests (dec-001..006)
- Reserved high-bit stripping, TCP fragmentation, round-trips all covered
- Build: 0 errors, 0 warnings; 32 new tests all green; 1 pre-existing RH-015 failure unchanged

---

## TASK-PSS-004: Replace Http2ProtocolSession — GoAway/Ping/RST Tests (RFC9113 §6.4/§6.7/§6.8)
**Status:** COMPLETE | **Date:** 2026-03-17

**Changes:**
- Rewrote `src/TurboHttp.Tests/RFC9113/07_ErrorHandlingTests.cs`
- Rewrote `src/TurboHttp.Tests/RFC9113/08_GoAwayTests.cs`
- Both files now use only `GoAwayFrame`, `PingFrame`, `RstStreamFrame`, `Http2FrameDecoder`
- No `Http2ProtocolSession` references remain in either file
- `07_ErrorHandlingTests.cs`: 25 old tests → 14 new tests (RST-001..007, PNG-001..007); all DisplayNames contain `RFC-9113-§6.4` or `RFC-9113-§6.7`
- `08_GoAwayTests.cs`: 20 old tests → 17 new tests (GA-001..011); all DisplayNames contain `RFC-9113-§6.8`
- Dropped session-state-dependent tests (stream tracking, flow control windows, MAX_CONCURRENT_STREAMS); those belong to stage-level tests
- Build: 0 errors, 0 warnings; 34 new tests all green; 1 pre-existing RH-015 failure unchanged

---

## TASK-PSS-005: Replace Http2ProtocolSession — Header/Pseudo-Header Tests (RFC9113 §8.2/§8.3)
**Status:** COMPLETE | **Date:** 2026-03-17

**Changes:**
- Rewrote `src/TurboHttp.Tests/RFC9113/06_HeadersTests.cs`
- Rewrote `src/TurboHttp.Tests/RFC9113/09_ContinuationFrameTests.cs`
- Rewrote `src/TurboHttp.Tests/RFC9113/11_DecoderStreamValidationTests.cs`
- All three files now use only `HpackDecoder`, `HpackEncoder`, `HeadersFrame`, `ContinuationFrame`, `Http2FrameDecoder`
- No `Http2ProtocolSession` references remain in any of the three files
- `06_HeadersTests.cs`: 28 old tests → 28 new tests; class `Http2DecoderHeadersValidationTests`; private `ValidateResponseHeaders()` helper enforces §8.2/§8.3 rules; all DisplayNames contain `RFC-9113-§8.2`, `RFC-9113-§8.3`, or `RFC-9113-§8.2.2`
- `09_ContinuationFrameTests.cs`: 25 old tests → 25 new tests; class `Http2ContinuationFrameTests`; private `AssembleHeaderBlock()` helper enforces §6.10 ordering rules; DisplayNames contain `RFC-9113-§8.2`
- `11_DecoderStreamValidationTests.cs`: 8 old session-state tests → 8 new header-block decoder tests; class renamed to `Http2HeaderBlockDecoderTests`; focuses on END_HEADERS flag, CONTINUATION chain assembly, and HPACK fragment decoding (§8.2/§8.3); DisplayNames contain `RFC-9113-§8.2` or `RFC-9113-§8.3`
- Scenarios covered: END_HEADERS flag (CF-001..004, HBD-001..004), CONTINUATION chain (CF-003..006, HBD-007), header block decoding (HV-001..028, HBD-005..008)
- Build: 0 errors, 0 warnings; all 80 target tests green; 1 pre-existing RH-015 failure unchanged

---

## TASK-PSS-006: Replace Http2ProtocolSession — Security/Fuzz/Concurrency Tests
**Status:** COMPLETE | **Date:** 2026-03-17

**Changes:**
- Rewrote all 6 files to remove Http2ProtocolSession:
  - `Http2SecurityTests.cs`: 6 old → 6 new tests; explicit flood enforcement helpers for CONTINUATION/RST/DATA floods + SETTINGS validation
  - `Http2FuzzHarnessTests.cs`: 25 old → 25 new tests; `AssertDecodeNeverCrashes()` now wraps `Http2FrameDecoder.Decode()`; window overflow tests use explicit enforcement
  - `Http2ResourceExhaustionTests.cs`: 38 old → 18 new tests; dropped session-only tests (Reset(), counter properties); kept all flood category tests with explicit counting + enforcement
  - `Http2HighConcurrencyTests.cs`: 20 old → 16 new tests; dropped 4 Reset() tests; parallel tests use independent decoder instances; flow control uses explicit window tracking
  - `Http2MaxConcurrentStreamsTests.cs`: 50 old → 38 new tests; dropped session defaults/Reset tests; added `ExtractMaxConcurrentStreams()`, `EnforceMaxConcurrentStreams()`, `TrackStreamState()` helpers
  - `Http2CrossComponentValidationTests.cs`: 20 old → 20 new tests; HPACK errors wrapped via `DecodeHpackWithCompressionErrorWrapping()`; flow control, RST, GOAWAY use explicit enforcement
- All DisplayNames contain RFC references (RFC-9113-§X.Y prefix)
- SEC-h2-XXX codes replaced with RFC-prefixed SEC-001..006
- Build: 0 errors, 0 warnings; all 123 target tests green; total 2069 tests in TurboHttp.Tests pass
- 1 pre-existing failure in StreamTests (ENG-002) unchanged

---

## Remaining Tasks

| Task | Status | Description |
|------|--------|-------------|
| TASK-PSS-001 | COMPLETE | Replace Http2ProtocolSession — Stream State Tests (§5.1) |
| TASK-PSS-002 | COMPLETE | Replace Http2ProtocolSession — Settings Tests (§6.5) |
| TASK-PSS-003 | COMPLETE | Replace Http2ProtocolSession — Flow Control Tests (§6.9) |
| TASK-PSS-004 | COMPLETE | Replace Http2ProtocolSession — GoAway/Ping/RST (§6.4/§6.7/§6.8) |
| TASK-PSS-005 | COMPLETE | Replace Http2ProtocolSession — Header/Pseudo-Header Tests (§8.2/§8.3) |
| TASK-PSS-006 | COMPLETE | Replace Http2ProtocolSession — Security/Fuzz/Concurrency |
| TASK-PSS-007 | PENDING | Delete Http2ProtocolSession (blocked by PSS-001..006 — now unblocked) |
| TASK-DISP-001 | PENDING | Add RFC References to Integration Test DisplayNames |
| TASK-DISP-002 | PENDING | Add RFC References to RFC9113 Tests Without Prefix |
| TASK-DISP-003 | PENDING | Add RFC References to StreamTests DisplayNames |
| TASK-SORT-001 | PENDING | Move Integration Test Files into RFC Folders |
| TASK-SORT-002 | PENDING | Restructure StreamTests into RFC Folders |
| TASK-SORT-003 | PENDING | Clean Up Loose Helper Files |
