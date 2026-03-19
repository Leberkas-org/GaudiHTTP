# Plan: Test Cleanup — Naming, Comments, Structure & Summaries

## Introduction

Comprehensive cleanup of all test projects: consistent method naming (`Should_Action_When_Condition`), consistent file naming (`NN_` prefix, no gaps, uniform prefixes), removal of redundant comments, reorganization of StreamTests into RFC folders, deletion of pseudo-integration tests from the unit test project, and XML summaries above every test class.

**Scope**: `src/TurboHttp.Tests/` (85 files, 7 RFC folders), `src/TurboHttp.StreamTests/` (73 files, 5 folders)

## Goals

- Consistent `Should_Action_When_Condition` naming across **all** test methods in both projects
- Consistent file naming: `NN_` prefix in TurboHttp.Tests (fill gaps, add missing prefixes), uniform `Http1X`/`Http20`/`Hpack` prefixes in StreamTests
- Remove all comments already covered by `DisplayName` or method name
- Keep technical comments (hex values, bit patterns, non-obvious logic)
- Reorganize StreamTests into RFC-based folder structure + `Pipeline/` and `Engine/` for non-RFC tests
- Delete integration test files from the unit test project
- Add `<summary>` + `<remarks>` XML documentation above every test class

## User Stories

---

### TASK-001: Define naming convention and create reference document

**Description:** As a developer, I want a clear reference for the naming schema so that all renames are applied consistently.

**Acceptance Criteria:**
- [ ] Reference document `.maggus/naming-convention.md` created with:
  - Old pattern → New pattern examples for every current variant
  - `Should_Action_When_Condition` defined as standard
  - Edge cases documented (e.g., `[Theory]` with `[InlineData]` — parameter in Condition part)
  - Examples for all RFC areas
- [ ] Rules for edge cases:
  - Parameterized tests: `Should_DecodeFrame_When_TypeIs(Http2FrameType type)`
  - Negative tests: `Should_ThrowHpackException_When_IndexExceedsTableSize`
  - Boundary tests: `Should_ReturnFalse_When_BufferHasLessThan9Bytes`

---

### TASK-001b: Standardize file names — TurboHttp.Tests

**Description:** As a developer, I want all test files in `TurboHttp.Tests` to follow the `NN_<ThemaTests>.cs` convention consistently — no files without prefix, no numbering gaps, no odd names.

**Current issues:**

| File | Problem | Proposed fix |
|------|---------|-------------|
| `RFC9112/Http11NegativePathTests.cs` | Missing `NN_` prefix | Rename to `24_NegativePathTests.cs` |
| `RFC9112/Http11DecoderChunkExtensionTests.cs` | Missing `NN_` prefix | Rename to `25_DecoderChunkExtensionTests.cs` |
| `RFC9112/Http11SecurityTests.cs` | Missing `NN_` prefix | Rename to `26_SecurityTests.cs` |
| `RFC9113/Http2FrameTests.cs` | Missing `NN_` prefix | Rename to `30_FrameTests.cs` |
| `RFC7541/HpackTests.cs` | Missing `NN_` prefix | Rename to `03_HpackRoundTripTests.cs` (fills gap) |
| `RFC1945/18_EncoderStageConversionExampleTests.cs` | Odd name "StageConversionExample" | Rename to `18_EncoderConversionTests.cs` |
| `RFC9113/` | Numbering gaps at 12, 17, 23 | Renumber contiguously (01–29) |
| `RFC7541/` | Numbering gap at 03 | Fill with `HpackTests.cs` rename |

**Acceptance Criteria:**
- [ ] Every test file in every RFC folder has `NN_` prefix
- [ ] No numbering gaps within any folder
- [ ] Class names inside files updated to match new file names
- [ ] `namespace` declarations remain unchanged
- [ ] Build + tests pass (`dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj`)

---

### TASK-002: Rename test methods — RFC1945 (17 files, ~232 tests)

**Description:** As a developer, I want all test methods in RFC1945 to follow `Should_Action_When_Condition`.

**Current pattern:** `RequestLine_ContainsExactlyOneSpaceBetweenParts()`, `Headers_HostHeader_IsRemovedForHttp10()`

**New pattern:** `Should_ContainOneSpaceBetweenParts_When_EncodingRequestLine()`, `Should_RemoveHostHeader_When_ProtocolIsHttp10()`

**Acceptance Criteria:**
- [ ] All test methods in `src/TurboHttp.Tests/RFC1945/` follow `Should_Action_When_Condition`
- [ ] `DisplayName` attributes remain unchanged
- [ ] All 232 tests compile and pass (`dotnet test --filter "FullyQualifiedName~RFC1945"`)
- [ ] No method names with `Test_` or `Subject_Verb_Result` prefix remaining

---

### TASK-003: Rename test methods — RFC9112 (26 files, ~379 tests)

**Description:** As a developer, I want all test methods in RFC9112 to follow `Should_Action_When_Condition`.

**Current pattern:** `Test_9112_RequestLine_UsesHttp11()`, `Test_Missing_Path_Normalized()`, `Test_Fragment_Stripped()`

**New pattern:** `Should_UseHttp11InRequestLine_When_Encoding()`, `Should_NormalizeMissingPath_When_PathIsEmpty()`, `Should_StripFragment_When_UriContainsFragment()`

**Acceptance Criteria:**
- [ ] All test methods in `src/TurboHttp.Tests/RFC9112/` follow `Should_Action_When_Condition`
- [ ] `DisplayName` attributes remain unchanged
- [ ] All 379 tests compile and pass (`dotnet test --filter "FullyQualifiedName~RFC9112"`)
- [ ] `Test_` prefix completely eliminated

---

### TASK-004: Rename test methods — RFC7541 (6 files, ~384 tests)

**Description:** As a developer, I want all test methods in RFC7541 to follow `Should_Action_When_Condition`.

**Current pattern:** `DynamicTable_Empty_HasSizeZero()`, `StaticTable_Count_IsExactly61()`

**New pattern:** `Should_HaveSizeZero_When_DynamicTableIsEmpty()`, `Should_ContainExactly61Entries_When_QueryingStaticTable()`

**Acceptance Criteria:**
- [ ] All test methods in `src/TurboHttp.Tests/RFC7541/` follow `Should_Action_When_Condition`
- [ ] `DisplayName` attributes remain unchanged
- [ ] All 384 tests compile and pass (`dotnet test --filter "FullyQualifiedName~RFC7541"`)

---

### TASK-005: Rename test methods — RFC9113 (28 files, ~580 tests)

**Description:** As a developer, I want all test methods in RFC9113 to follow `Should_Action_When_Condition`.

**Current pattern:** `FrameHeader_ZeroBytes_ReturnsFalse()`, `FrameHeader_Exactly9BytesEmptyPayload_IsDecoded()`

**New pattern:** `Should_ReturnFalse_When_FrameHeaderHasZeroBytes()`, `Should_DecodeFrame_When_PayloadIsExactly9Bytes()`

**Acceptance Criteria:**
- [ ] All test methods in `src/TurboHttp.Tests/RFC9113/` follow `Should_Action_When_Condition`
- [ ] `DisplayName` attributes remain unchanged
- [ ] All 580 tests compile and pass (`dotnet test --filter "FullyQualifiedName~RFC9113"`)

---

### TASK-006: Rename test methods — RFC9110, RFC9111, RFC6265 (9 files, ~252 tests)

**Description:** As a developer, I want all test methods in the remaining RFC folders renamed consistently.

**Current pattern (mixed):**
- RFC9110: `IsRedirect_Returns_True_For_Redirect_Status_Codes()`, `Should_Retry_When_GET_And_NetworkFailure()`
- RFC9111: `NullInput_ReturnsNull()`, `MaxAge_FreshnessLifetime_60s()`
- RFC6265: `Basic_Cookie_Is_Stored()`, `HostOnly_Cookie_Matches_Exact_Host_Only()`

**New pattern:**
- `Should_ReturnTrue_When_StatusCodeIsRedirect()`
- `Should_RetryRequest_When_MethodIsGetAndNetworkFails()`
- `Should_ReturnNull_When_InputIsNull()`
- `Should_StoreCookie_When_BasicNameValuePairProvided()`

**Acceptance Criteria:**
- [ ] All test methods in RFC9110/, RFC9111/, RFC6265/ follow `Should_Action_When_Condition`
- [ ] `DisplayName` attributes remain unchanged
- [ ] All 252 tests compile and pass
- [ ] Existing `Should_` methods in RFC9110 adjusted if `When_` part is missing

---

### TASK-007: Remove redundant comments — TurboHttp.Tests (all RFC folders)

**Description:** As a developer, I want all comments removed that are already covered by `DisplayName` or method name, so the code is cleaner.

**Remove (examples):**
- `// Transfer-Encoding ist HTTP/1.1 (RFC 2616 §14.41)` → DisplayName already says this
- `// HTTP/1.0 default: no Connection header means close` → method name covers this after rename
- Section header comments like `// ── CM-001–CM-005: Basic cookie parsing ──` → unnecessary when file is named by topic

**Keep (examples):**
- `// 0x82 = 10000010 → index 2 (:method, GET)` → hex/bit explanation, not obvious from code
- `// "via" = 3 bytes, "proxy1" = 6 bytes → 3+6+32 = 41` → calculation explanation
- `// POST with empty body must emit Content-Length: 0 so that HTTP/1.0 servers do not wait...` → technical reason

**Acceptance Criteria:**
- [ ] All comments duplicating DisplayName/method name are removed
- [ ] Section header comments (`// ──` and `// ===`) removed
- [ ] Hex value explanations, bit pattern comments, and non-obvious technical explanations preserved
- [ ] No test has lost its context — when in doubt, keep the comment
- [ ] Build + tests pass

---

### TASK-008: Add XML summaries — TurboHttp.Tests (all test classes)

**Description:** As a developer, I want XML documentation with `<summary>` and `<remarks>` above every test class, so it's immediately clear what is tested and which RFC section is covered.

**Format:**
```csharp
/// <summary>
/// Tests HTTP/1.0 request-line encoding per RFC 1945 §5.1.
/// Verifies method, URI, and protocol version serialization.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http10Encoder"/>.
/// RFC 1945 §5.1: Request-Line = Method SP Request-URI SP HTTP-Version CRLF.
/// </remarks>
public sealed class Http10EncoderRequestLineTests
```

**Acceptance Criteria:**
- [ ] Every test class in `src/TurboHttp.Tests/` has `<summary>` (what is tested, 1-2 lines)
- [ ] Every test class has `<remarks>` with:
  - `<see cref="..."/>` to the class under test
  - RFC section with short specification description
- [ ] Format is consistent across all 85+ files
- [ ] Build passes (valid XML documentation)

---

### TASK-009: Delete integration tests from unit test project

**Description:** As a developer, I want the pseudo-integration tests removed from `TurboHttp.Tests` because they are not real integration tests and the project is intended for unit tests only.

**Affected files:**
- `src/TurboHttp.Tests/RFC1945/05_EncoderIntegrationTests.cs`
- `src/TurboHttp.Tests/RFC9110/03_ContentEncodingIntegrationTests.cs`
- `src/TurboHttp.Tests/RFC9111/05_CacheIntegrationTests.cs`

**Acceptance Criteria:**
- [ ] All 3 files deleted
- [ ] Build passes (`dotnet build src/TurboHttp.sln`)
- [ ] Remaining tests all pass
- [ ] RFC_COVERAGE.md updated (test counts corrected)
- [ ] CLAUDE.md test table updated (file counts corrected)

---

### TASK-009b: Standardize file names — StreamTests

**Description:** As a developer, I want all StreamTests files to use consistent naming — uniform protocol prefixes and no cryptic abbreviations.

**Current issues:**

| File | Problem | Proposed fix |
|------|---------|-------------|
| `PrependPrefaceStageTests.cs` | Missing `Http20` prefix | `Http20PrependPrefaceStageTests.cs` |
| `Request2FrameStageTests.cs` | `2` instead of `To`, missing prefix | `Http20RequestToFrameStageTests.cs` |
| `StreamIdAllocatorStageTests.cs` | Missing `Http20` prefix | `Http20StreamIdAllocatorStageTests.cs` |
| `Http1XCorrelationStageTests.cs` | `Http1X` prefix inconsistent | `Http11CorrelationStageTests.cs` (lives in RFC9112/) |

**Acceptance Criteria:**
- [ ] All HTTP/1.0 stage test files start with `Http10`
- [ ] All HTTP/1.1 stage test files start with `Http11`
- [ ] All HTTP/2 stage test files start with `Http20`
- [ ] No `2` abbreviation for `To` in file names
- [ ] Class names inside files updated to match new file names
- [ ] Build + tests pass

---

### TASK-010: Reorganize StreamTests folder structure — create RFC folders

**Description:** As a developer, I want StreamTests organized into RFC-based folders so the structure is consistent with the unit test project.

**New structure:**
```
src/TurboHttp.StreamTests/
├── RFC1945/          (from Http10/)
│   ├── Http10DecoderStageRfcTests.cs
│   ├── Http10DecoderStageTests.cs
│   ├── Http10EncoderStageRfcTests.cs
│   ├── Http10EncoderStageTests.cs
│   ├── Http10StageRoundTripHeaderBodyTests.cs
│   ├── Http10StageRoundTripMethodTests.cs
│   └── Http10StageTcpFragmentationTests.cs
│
├── RFC9112/          (from Http11/)
│   ├── Http11BatchEncodingTests.cs
│   ├── Http11DecoderStageChunkedRfcTests.cs
│   ├── Http11DecoderStageTests.cs
│   ├── Http11EncoderStageRfcTests.cs
│   ├── Http11EncoderStageTests.cs
│   ├── Http11ResponseCorrelationTests.cs
│   ├── Http11StageConnectionMgmtTests.cs
│   ├── Http11StageFragmentationTests.cs
│   ├── Http11StageRoundTripPipelineTests.cs
│   ├── Http11StageStatusCodeTests.cs
│   ├── Http11CorrelationStageTests.cs  (renamed from Http1XCorrelationStageTests)
│   └── ConnectionReuseStageTests.cs  (from Streams/)
│
├── RFC9113/          (from Http20/ — HTTP/2 specific)
│   ├── Http20BatchEncodingTests.cs
│   ├── Http20ConnectionPrefaceRfcTests.cs
│   ├── Http20ConnectionStageBackpressureTests.cs
│   ├── Http20ConnectionStageFlowControlTests.cs
│   ├── Http20ConnectionStageGoAwayTests.cs
│   ├── Http20ConnectionStagePingTests.cs
│   ├── Http20ConnectionStageSettingsTests.cs
│   ├── Http20ConnectionStageStreamAcquireTests.cs
│   ├── Http20CorrelationStageTests.cs
│   ├── Http20DecoderStageRfcTests.cs
│   ├── Http20DecoderStageTests.cs
│   ├── Http20EncoderStageRfcTests.cs
│   ├── Http20EncoderStageTests.cs
│   ├── Http20ForbiddenHeaderRfcTests.cs
│   ├── Http20PseudoHeaderRfcTests.cs
│   ├── Http20StreamIdRfcTests.cs
│   ├── Http20StreamStageMemoryTests.cs
│   ├── Http20StreamStageTests.cs
│   ├── Http20PrependPrefaceStageTests.cs
│   ├── Http20RequestToFrameStageTests.cs
│   └── Http20StreamIdAllocatorStageTests.cs
│
├── RFC7541/          (from Http20/ — HPACK specific)
│   └── Http20HpackStreamTests.cs
│
├── RFC9110/          (from Streams/ — Semantics)
│   ├── DecompressionStageTests.cs
│   ├── RedirectStageTests.cs
│   └── RetryStageTests.cs
│
├── RFC9111/          (from Streams/ — Caching)
│   ├── CacheLookupStageTests.cs
│   └── CacheStorageStageTests.cs
│
├── RFC6265/          (from Streams/ — Cookies)
│   ├── CookieInjectionStageTests.cs
│   └── CookieStorageStageTests.cs
│
├── Engine/           (from Streams/ + engine round-trip files — no direct RFC)
│   ├── Http10EngineRfcRoundTripTests.cs  (from Http10/)
│   ├── Http11EngineRfcRoundTripTests.cs  (from Http11/)
│   ├── Http20EngineRfcRoundTripTests.cs  (from Http20/)
│   ├── EnginePipelineWiringTests.cs
│   ├── EngineVersionRoutingTests.cs
│   ├── RequestEnricherStageTests.cs
│   └── ExtractOptionsStageTests.cs
│
├── Pipeline/         (from Streams/ — pipeline infrastructure, no direct RFC)
│   ├── AsyncBoundaryTests.cs
│   ├── ConnectionStageTests.cs
│   ├── FeedbackBufferOptimizationTests.cs
│   ├── GroupByHostKeyQueueSizeTests.cs
│   ├── HostKeySubFlowTests.cs
│   ├── MaterializerBufferTuningTests.cs
│   └── LoopbackBenchmarkStageTests.cs
│
├── IO/               (unchanged — Actor/Channel layer)
│   ├── ConnectionActorTests.cs
│   ├── ConnectionHandleTests.cs
│   ├── ConnectionStateTests.cs
│   ├── HostPoolActorEnsureHostTests.cs
│   ├── HostPoolActorSelectConnectionTests.cs
│   ├── HostPoolActorStreamLifecycleTests.cs
│   ├── HostPoolActorTests.cs
│   └── IoActorTestBase.cs
│
├── Stages/           (unchanged — cross-version stage behavior)
│   ├── DecoderStagePartialTests.cs
│   ├── EncoderStageBufferTests.cs
│   └── StageLifecycleTests.cs
│
├── EngineTestBase.cs
├── StreamTestBase.cs
└── SimpleMemoryOwner.cs
```

**Acceptance Criteria:**
- [x] All files moved to new folders
- [x] Namespaces updated in all moved files (e.g., `TurboHttp.StreamTests.RFC1945`)
- [x] Old folders `Http10/`, `Http11/`, `Http20/`, `Streams/` deleted
- [x] `ConnectionReuseStageTests.cs` placed in `RFC9112/`
- [x] Build passes (`dotnet build src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj`)
- [x] All StreamTests pass

---

### TASK-011: Rename test methods — StreamTests (all folders)

**Description:** As a developer, I want all test methods in StreamTests to follow `Should_Action_When_Condition` — consistent with the unit tests.

**Acceptance Criteria:**
- [ ] All test methods in StreamTests follow `Should_Action_When_Condition`
- [ ] `DisplayName` attributes remain unchanged
- [ ] All StreamTests compile and pass
- [ ] IO/ and Stages/ folders also renamed

---

### TASK-012: Remove redundant comments — StreamTests

**Description:** As a developer, I want redundant comments in StreamTests removed (same rules as TASK-007).

**Acceptance Criteria:**
- [ ] Comments duplicating DisplayName/method name removed
- [ ] Section header comments removed
- [ ] Technical comments (hex, bit patterns, Akka-specific explanations) preserved
- [ ] Build + tests pass

---

### TASK-013: Add XML summaries — StreamTests (all test classes)

**Description:** As a developer, I want XML summaries above every StreamTest class.

**Format:**
```csharp
/// <summary>
/// Tests HTTP/2 SETTINGS frame negotiation in the connection stage.
/// Validates initial settings exchange and parameter acknowledgment per RFC 9113 §6.5.
/// </summary>
/// <remarks>
/// Stage under test: <see cref="Http20ConnectionStage"/>.
/// RFC 9113 §6.5: SETTINGS frames convey configuration parameters.
/// Each endpoint MUST send a SETTINGS frame as the first frame of the connection preface.
/// </remarks>
public sealed class Http20ConnectionStageSettingsTests : StreamTestBase
```

**Acceptance Criteria:**
- [ ] Every test class in StreamTests has `<summary>` + `<remarks>`
- [ ] `<see cref="..."/>` references the stage/class under test
- [ ] Format is consistent across all ~73 files
- [ ] Build passes

---

### TASK-014: Update documentation

**Description:** As a developer, I want CLAUDE.md and RFC_COVERAGE.md updated to reflect the new structure.

**Acceptance Criteria:**
- [ ] `CLAUDE.md` — Test organization table updated:
  - File counts corrected (integration tests deleted)
  - StreamTests structure documented (new RFC folders + Engine/ + Pipeline/)
  - Naming convention `Should_Action_When_Condition` documented
- [ ] `RFC_COVERAGE.md` — Test counts corrected
- [ ] Build passes

---

## Functional Requirements

- FR-1: Every test method in both projects follows `Should_[Action]_When_[Condition]`
- FR-2: `DisplayName` attributes are NEVER modified during renames
- FR-3: Comments are only removed when `DisplayName` or method name already carry the same information
- FR-4: Hex explanations (`// 0x82 = ...`), bit patterns, and calculations are always preserved
- FR-5: Every test class has `/// <summary>` + `/// <remarks>` with `<see cref="..."/>` to the SUT class
- FR-6: StreamTests are organized by RFC folders, namespaces updated accordingly
- FR-7: Integration test files in the unit test project are deleted
- FR-8: After each TASK, all tests in the affected area must be green
- FR-9: Every test file in `TurboHttp.Tests/` RFC folders has a contiguous `NN_` prefix
- FR-10: Every StreamTests file uses a uniform protocol prefix (`Http10`, `Http11`, `Http20`, `Hpack`)

## Non-Goals

- No changes to production code (`src/TurboHttp/`)
- No changes to `DisplayName` attributes (they stay as-is)
- No new tests — only rename/move existing ones
- No changes to the `TurboHttp.IntegrationTests` project
- No changes to test logic or assertions
- No restructuring of unit test folders in `TurboHttp.Tests/` (RFC folders there stay as-is)

## Technical Considerations

- **Namespace changes in StreamTests**: When moving files, `namespace` declarations must be updated (e.g., `TurboHttp.StreamTests.Http10` → `TurboHttp.StreamTests.RFC1945`)
- **Execution order**: File renames first (TASK-001b, TASK-009b), then method renames (TASK-002–006), then comments/summaries (TASK-007–008), then StreamTests restructure (TASK-009–013), then docs (TASK-014)
- **Parallelization**: TASK-002 through TASK-006 (method naming per RFC) can run in parallel. TASK-009b (file renames) should run before TASK-010 (folder moves) to avoid double-renaming. TASK-001b and TASK-009b can run in parallel.
- **Git strategy**: One commit per TASK for clean history and easy revert if needed
- **csproj**: The StreamTests `.csproj` should not need changes — SDK-style projects auto-include all `.cs` files

## Success Metrics

- 0 test methods with old naming patterns (`Test_`, `Subject_Verb_Result` without `Should_`)
- 0 test files in `TurboHttp.Tests/` without `NN_` prefix
- 0 StreamTests files with missing or inconsistent protocol prefix
- 0 redundant comments duplicating DisplayName/method name
- 100% of test classes have XML `<summary>` + `<remarks>`
- StreamTests follow the same RFC folder structure as unit tests
- No integration test files remaining in the unit test project
- All ~2100+ tests green after completion

## Open Questions

- Should `ConnectionReuseStageTests.cs` go into `RFC9112/` or a separate `RFC9112_Connection/` folder? (Plan proposes `RFC9112/` since it's only 1 file)
- Should `*EngineRfcRoundTripTests` go into their respective RFC folders (RFC1945/, RFC9112/, RFC9113/) or into `Engine/`? (Plan proposes `Engine/` since they test engine infrastructure)
- How to handle `Http1XCorrelationStageTests.cs` which covers both HTTP/1.0 and 1.1? (Plan: put in `RFC9112/` since HTTP/1.1 is the primary context)
