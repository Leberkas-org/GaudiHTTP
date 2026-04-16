<!-- maggus-id: 0d15f4e4-a80c-481c-beeb-6c4ccb602e6a -->

# Feature 001: Integration Test Migration to Deterministic StreamTests

## Introduction

Migrate all 84 IntegrationTest files (83 acceptance + 1 unit) to deterministic StreamTests and UnitTests, achieving 1:1 coverage parity. Every integration test method gets a stream test equivalent with the same protocol variant, same scenarios, and same assertions — without hitting a real Kestrel server, real TCP, or real QUIC.

### Architecture Context

- **Components involved:** `TurboHTTP.StreamTests` (acceptance tier), `TurboHTTP.Tests` (unit tier), `TurboHTTP.IntegrationTests` (source, archived after migration)
- **Existing patterns extended:** `EngineTestBase`, `EngineFakeConnectionStage`, `H2EngineFakeConnectionStage`, `H3EngineFakeConnectionStage`, `StreamTestBase`
- **New infrastructure:** `ScriptedFakeConnectionStage` (byte-level with request-index routing), `ResponseMap`/`ResponseMapFake` (protocol-level `BidiFlow` fake), `H2ResponseBuilder`/`H3ResponseBuilder` (fluent frame builders), `FakeProxyStage` (CONNECT tunnel simulation)
- **Architecture alignment:** Uses Akka.Streams `BidiFlow` composition and `GraphStage` patterns. Fakes replace transport at the same injection point as `ITransportFactory`, maintaining layer boundaries.

## Goals

- Eliminate all flaky tests caused by real network/server dependencies
- Achieve 1:1 test method parity with every integration test
- Keep all tests deterministic — no network, no timing, no port conflicts
- Produce 83 acceptance StreamTest files + 1 UnitTest file (LoggingBridgeSpec)
- Archive IntegrationTests project (remove from CI, keep in solution)

## Tasks

### TASK-001-001: ScriptedFakeConnectionStage Infrastructure
**Description:** As a test author, I want a `ScriptedFakeConnectionStage` that accepts `Func<int, byte[], byte[]>` (request index + outbound bytes → response bytes) so that I can write multi-response and conditional-response byte-level acceptance tests.

**Token Estimate:** ~60k tokens
**Predecessors:** none
**Successors:** TASK-001-004 through TASK-001-016
**Parallel:** yes — can run alongside TASK-001-002, TASK-001-003
**Model:** opus

**Acceptance Criteria:**
- [ ] `ScriptedFakeConnectionStage` created in `StreamTests/Acceptance/Shared/`
- [ ] Extends `GraphStage<FlowShape<IOutputItem, IInputItem>>` matching `EngineFakeConnectionStage` shape
- [ ] Accepts `Func<int, byte[], byte[]>` with request counter
- [ ] Supports multi-response sequences (connection reuse)
- [ ] Supports error injection (truncated body, abort mid-stream, corrupt bytes) via response factory
- [ ] Exposes `OutboundChannel` for request inspection (same as existing fake)
- [ ] Unit test verifying multi-response sequencing works
- [ ] Unit test verifying error injection (truncated response) works
- [ ] Build passes, existing tests unaffected

---

### TASK-001-002: ResponseMap + ResponseMapFake Infrastructure
**Description:** As a test author, I want a protocol-level fake (`ResponseMap` + `ResponseMapFake`) that maps `HttpRequestMessage → HttpResponseMessage` so that I can write feature-logic tests (cookies, redirects, cache, retry) without byte crafting.

**Token Estimate:** ~80k tokens
**Predecessors:** none
**Successors:** TASK-001-008 through TASK-001-016
**Parallel:** yes — can run alongside TASK-001-001, TASK-001-003
**Model:** opus

**Acceptance Criteria:**
- [ ] `ResponseMap` builder class in `StreamTests/Acceptance/Shared/` with fluent `.On(path, status, body)` and `.On(path, Func<HttpRequestMessage, HttpResponseMessage>)` overloads
- [ ] `ResponseMapFake` is a `BidiFlow<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage, NotUsed>` that applies the map
- [ ] Default 404 response for unmapped paths
- [ ] Supports header manipulation via builder callback
- [ ] Unit test verifying simple GET → 200 mapping
- [ ] Unit test verifying dynamic response (request-dependent)
- [ ] Unit test verifying unmapped path returns 404
- [ ] Build passes

---

### TASK-001-003: H2ResponseBuilder + H3ResponseBuilder Infrastructure
**Description:** As a test author, I want fluent helpers for constructing HTTP/2 and HTTP/3 frame-level byte arrays so that byte-crafting for H2/H3 acceptance tests is less verbose and less error-prone.

**Token Estimate:** ~70k tokens
**Predecessors:** none
**Successors:** TASK-001-006, TASK-001-007
**Parallel:** yes — can run alongside TASK-001-001, TASK-001-002
**Model:** opus

**Acceptance Criteria:**
- [ ] `H2ResponseBuilder` in `StreamTests/Acceptance/Shared/` with fluent API: `.Settings()`, `.Headers(streamId, status, headers)`, `.Data(streamId, body, endStream)`, `.WindowUpdate()`, `.Build()` returning `byte[]`
- [ ] `H3ResponseBuilder` in `StreamTests/Acceptance/Shared/` with equivalent fluent API adapted for HTTP/3 frames (QPACK-encoded headers)
- [ ] Builders produce valid frames decodable by existing `FrameDecoder` implementations
- [ ] Unit test: H2 builder produces valid SETTINGS + HEADERS + DATA sequence
- [ ] Unit test: H3 builder produces valid SETTINGS + HEADERS + DATA sequence
- [ ] Build passes

---

### TASK-001-004: H10 Smoke + Connection + Compression + EdgeCase + ErrorHandling + Resilience + Concurrency Acceptance Tests
**Description:** As a test author, I want the byte-level H10 acceptance tests migrated so that all HTTP/1.0 wire-format scenarios are covered deterministically.

**Token Estimate:** ~150k tokens
**Predecessors:** TASK-001-001
**Successors:** TASK-001-017
**Parallel:** yes — can run alongside TASK-001-005, TASK-001-006, TASK-001-007
**Model:** opus

**Acceptance Criteria:**
- [ ] `StreamTests/Acceptance/H10/SmokeSpec.cs` — 1:1 parity with `IntegrationTests/H10/SmokeSpec.cs`
- [ ] `StreamTests/Acceptance/H10/ConnectionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H10/CompressionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H10/EdgeCaseSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H10/ErrorHandlingSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H10/ResilienceSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H10/ConcurrencySpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H10/RequestCompressionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H10/ExpectContinueSpec.cs` — 1:1 parity
- [ ] All tests use `ScriptedFakeConnectionStage` (byte-level)
- [ ] Same method names, same assertions as integration tests
- [ ] All tests green, build passes
- [ ] `[Trait("RFC", "...")]` traceability where applicable

---

### TASK-001-005: H11 Smoke + Connection + Compression + EdgeCase + ErrorHandling + Resilience + Concurrency Acceptance Tests
**Description:** As a test author, I want the byte-level H11 acceptance tests migrated so that all HTTP/1.1 wire-format scenarios are covered deterministically.

**Token Estimate:** ~150k tokens
**Predecessors:** TASK-001-001
**Successors:** TASK-001-017
**Parallel:** yes — can run alongside TASK-001-004, TASK-001-006, TASK-001-007
**Model:** opus

**Acceptance Criteria:**
- [ ] `StreamTests/Acceptance/H11/SmokeSpec.cs` — 1:1 parity with `IntegrationTests/H11/SmokeSpec.cs`
- [ ] `StreamTests/Acceptance/H11/ConnectionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H11/CompressionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H11/EdgeCaseSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H11/ErrorHandlingSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H11/ResilienceSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H11/ConcurrencySpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H11/RequestCompressionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H11/ExpectContinueSpec.cs` — 1:1 parity
- [ ] All tests use `ScriptedFakeConnectionStage` (byte-level)
- [ ] Same method names, same assertions as integration tests
- [ ] All tests green, build passes

---

### TASK-001-006: H2 Smoke + Connection + Compression + EdgeCase + ErrorHandling + Resilience + Concurrency + MaxConcurrentStreams Acceptance Tests
**Description:** As a test author, I want the frame-level H2 acceptance tests migrated so that all HTTP/2 wire-format scenarios are covered deterministically.

**Token Estimate:** ~180k tokens
**Predecessors:** TASK-001-001, TASK-001-003
**Successors:** TASK-001-017
**Parallel:** yes — can run alongside TASK-001-004, TASK-001-005, TASK-001-007
**Model:** opus

**Acceptance Criteria:**
- [ ] `StreamTests/Acceptance/H2/SmokeSpec.cs` — 1:1 parity with `IntegrationTests/H2/SmokeSpec.cs`
- [ ] `StreamTests/Acceptance/H2/ConnectionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H2/CompressionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H2/EdgeCaseSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H2/ErrorHandlingSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H2/ResilienceSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H2/ConcurrencySpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H2/RequestCompressionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H2/ExpectContinueSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H2/MaxConcurrentStreamsSpec.cs` — 1:1 parity
- [ ] All tests use `H2EngineFakeConnectionStage` + `H2ResponseBuilder` (frame-level)
- [ ] Same method names, same assertions as integration tests
- [ ] All tests green, build passes

---

### TASK-001-007: H3 Smoke + Connection + Compression + EdgeCase + ErrorHandling + Resilience + Concurrency + MaxStreamConcurrency Acceptance Tests
**Description:** As a test author, I want the frame-level H3 acceptance tests migrated so that all HTTP/3 wire-format scenarios are covered deterministically.

**Token Estimate:** ~180k tokens
**Predecessors:** TASK-001-001, TASK-001-003
**Successors:** TASK-001-017
**Parallel:** yes — can run alongside TASK-001-004, TASK-001-005, TASK-001-006
**Model:** opus

**Acceptance Criteria:**
- [ ] `StreamTests/Acceptance/H3/SmokeSpec.cs` — 1:1 parity with `IntegrationTests/H3/SmokeSpec.cs`
- [ ] `StreamTests/Acceptance/H3/ConnectionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H3/CompressionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H3/EdgeCaseSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H3/ErrorHandlingSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H3/ResilienceSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H3/ConcurrencySpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H3/RequestCompressionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H3/ExpectContinueSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H3/MaxStreamConcurrencySpec.cs` — 1:1 parity
- [ ] All tests use `H3EngineFakeConnectionStage` + `H3ResponseBuilder` (frame-level)
- [ ] Same method names, same assertions as integration tests
- [ ] All tests green, build passes

---

### TASK-001-008: H10 Cookie + Redirect + Retry + Cache + FeatureInteraction + Options Acceptance Tests
**Description:** As a test author, I want the protocol-level H10 feature-logic tests migrated so that all HTTP/1.0 feature-composition scenarios are covered deterministically.

**Token Estimate:** ~120k tokens
**Predecessors:** TASK-001-002, TASK-001-004
**Successors:** TASK-001-017
**Parallel:** yes — can run alongside TASK-001-009, TASK-001-010, TASK-001-011, TASK-001-012

**Acceptance Criteria:**
- [ ] `StreamTests/Acceptance/H10/CookieSpec.cs` — 1:1 parity with `IntegrationTests/H10/CookieSpec.cs`
- [ ] `StreamTests/Acceptance/H10/RedirectSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H10/RetrySpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H10/CacheSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H10/FeatureInteractionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H10/OptionsSpec.cs` — 1:1 parity
- [ ] All tests use `ResponseMapFake` (protocol-level)
- [ ] Same method names, same assertions as integration tests
- [ ] All tests green, build passes

---

### TASK-001-009: H11 Cookie + Redirect + RedirectSecurity + Retry + Cache + FeatureInteraction + Options + HandlerPipeline Acceptance Tests
**Description:** As a test author, I want the protocol-level H11 feature-logic tests migrated so that all HTTP/1.1 feature-composition scenarios are covered deterministically.

**Token Estimate:** ~150k tokens
**Predecessors:** TASK-001-002, TASK-001-005
**Successors:** TASK-001-017
**Parallel:** yes — can run alongside TASK-001-008, TASK-001-010, TASK-001-011, TASK-001-012

**Acceptance Criteria:**
- [ ] `StreamTests/Acceptance/H11/CookieSpec.cs` — 1:1 parity with `IntegrationTests/H11/CookieSpec.cs`
- [ ] `StreamTests/Acceptance/H11/RedirectSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H11/RedirectSecuritySpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H11/RetrySpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H11/CacheSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H11/FeatureInteractionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H11/OptionsSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H11/HandlerPipelineSpec.cs` — 1:1 parity
- [ ] All tests use `ResponseMapFake` (protocol-level)
- [ ] Same method names, same assertions as integration tests
- [ ] All tests green, build passes

---

### TASK-001-010: H2 Cookie + Redirect + Retry + Cache + FeatureInteraction + Options + HandlerPipeline Acceptance Tests
**Description:** As a test author, I want the protocol-level H2 feature-logic tests migrated so that all HTTP/2 feature-composition scenarios are covered deterministically.

**Token Estimate:** ~140k tokens
**Predecessors:** TASK-001-002, TASK-001-006
**Successors:** TASK-001-017
**Parallel:** yes — can run alongside TASK-001-008, TASK-001-009, TASK-001-011, TASK-001-012

**Acceptance Criteria:**
- [ ] `StreamTests/Acceptance/H2/CookieSpec.cs` — 1:1 parity with `IntegrationTests/H2/CookieSpec.cs`
- [ ] `StreamTests/Acceptance/H2/RedirectSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H2/RetrySpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H2/CacheSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H2/FeatureInteractionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H2/OptionsSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H2/HandlerPipelineSpec.cs` — 1:1 parity
- [ ] All tests use `ResponseMapFake` (protocol-level)
- [ ] Same method names, same assertions as integration tests
- [ ] All tests green, build passes

---

### TASK-001-011: H3 Cookie + Redirect + Retry + Cache + FeatureInteraction + Options + HandlerPipeline Acceptance Tests
**Description:** As a test author, I want the protocol-level H3 feature-logic tests migrated so that all HTTP/3 feature-composition scenarios are covered deterministically.

**Token Estimate:** ~140k tokens
**Predecessors:** TASK-001-002, TASK-001-007
**Successors:** TASK-001-017
**Parallel:** yes — can run alongside TASK-001-008, TASK-001-009, TASK-001-010, TASK-001-012

**Acceptance Criteria:**
- [ ] `StreamTests/Acceptance/H3/CookieSpec.cs` — 1:1 parity with `IntegrationTests/H3/CookieSpec.cs`
- [ ] `StreamTests/Acceptance/H3/RedirectSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H3/RetrySpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H3/CacheSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H3/FeatureInteractionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H3/OptionsSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/H3/HandlerPipelineSpec.cs` — 1:1 parity
- [ ] All tests use `ResponseMapFake` (protocol-level)
- [ ] Same method names, same assertions as integration tests
- [ ] All tests green, build passes

---

### TASK-001-012: TLS Acceptance Tests (All 15 files)
**Description:** As a test author, I want all TLS integration tests migrated so that TLS-specific scenarios (certificate validation, HTTPS redirect security) are covered deterministically.

**Token Estimate:** ~200k tokens
**Predecessors:** TASK-001-001, TASK-001-002
**Successors:** TASK-001-014, TASK-001-017
**Parallel:** yes — can run alongside TASK-001-008 through TASK-001-011
**Model:** opus

**Acceptance Criteria:**
- [ ] `StreamTests/Acceptance/TLS/SmokeSpec.cs` — 1:1 parity with `IntegrationTests/TLS/SmokeSpec.cs`
- [ ] `StreamTests/Acceptance/TLS/ConnectionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/TLS/CompressionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/TLS/RequestCompressionSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/TLS/CookieSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/TLS/RedirectSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/TLS/RedirectSecuritySpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/TLS/RetrySpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/TLS/CacheSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/TLS/ExpectContinueSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/TLS/ErrorHandlingSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/TLS/ResilienceSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/TLS/IntegrationSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/TLS/OptionsSpec.cs` — 1:1 parity
- [ ] `StreamTests/Acceptance/TLS/FeatureInteractionTlsSpec.cs` — 1:1 parity
- [ ] TLS tests use `CertificateValidation` callback injection (pattern from `Http30CertificateValidationSpec`)
- [ ] Byte-level wire tests use `ScriptedFakeConnectionStage`, feature-logic tests use `ResponseMapFake`
- [ ] Same method names, same assertions as integration tests
- [ ] All tests green, build passes

---

### TASK-001-013: FakeProxyStage Infrastructure
**Description:** As a test author, I want a `FakeProxyStage` that simulates CONNECT tunnel handshake at the transport level so that Proxy acceptance tests work without a real proxy server.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-001-001
**Successors:** TASK-001-014
**Parallel:** yes — can run alongside TASK-001-004 through TASK-001-012

**Acceptance Criteria:**
- [ ] `FakeProxyStage` created in `StreamTests/Acceptance/Shared/`
- [ ] Intercepts `ConnectItem`, responds with `200 Connection Established` bytes
- [ ] Passes through to an inner byte-level fake after tunnel is established
- [ ] Unit test verifying CONNECT handshake + tunneled request works
- [ ] Build passes

---

### TASK-001-014: Proxy Acceptance Tests
**Description:** As a test author, I want the Proxy integration tests migrated so that CONNECT tunnel and relay scenarios are covered deterministically.

**Token Estimate:** ~80k tokens
**Predecessors:** TASK-001-013, TASK-001-012
**Successors:** TASK-001-017
**Parallel:** no — depends on TLS + FakeProxyStage
**Model:** opus

**Acceptance Criteria:**
- [ ] `StreamTests/Acceptance/Proxy/ProxyConnectSpec.cs` — 1:1 parity with `IntegrationTests/Proxy/ProxyConnectSpec.cs`
- [ ] `StreamTests/Acceptance/Proxy/ProxyRelaySpec.cs` — 1:1 parity with `IntegrationTests/Proxy/ProxyRelaySpec.cs`
- [ ] Tests use `FakeProxyStage` wrapping inner protocol fake
- [ ] Same method names, same assertions as integration tests
- [ ] All tests green, build passes

---

### TASK-001-015: LoggingBridgeSpec Migration to UnitTests
**Description:** As a test author, I want `LoggingBridgeSpec` migrated from IntegrationTests to UnitTests since it tests logging bridge logic, not protocol behavior.

**Token Estimate:** ~20k tokens
**Predecessors:** none
**Successors:** TASK-001-017
**Parallel:** yes — can run alongside any other task

**Acceptance Criteria:**
- [ ] `TurboHTTP.Tests/LoggingBridgeSpec.cs` created with 1:1 parity
- [ ] No network or server dependencies
- [ ] Original `IntegrationTests/LoggingBridgeSpec.cs` marked `[Obsolete]`
- [ ] Test green, build passes

---

### TASK-001-016: AcceptanceTestBase Helper Class
**Description:** As a test author, I want a shared `AcceptanceTestBase` that extends `EngineTestBase` with helpers for both `ScriptedFakeConnectionStage` and `ResponseMapFake` pipelines, reducing boilerplate across all 83 acceptance test files.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-001-001, TASK-001-002
**Successors:** TASK-001-004 through TASK-001-014
**Parallel:** no — infrastructure that all protocol tasks depend on
**Model:** opus

**Acceptance Criteria:**
- [ ] `StreamTests/Acceptance/Shared/AcceptanceTestBase.cs` created
- [ ] Provides `SendScriptedAsync(engine, request, Func<int, byte[], byte[]>)` helper
- [ ] Provides `SendWithFakeAsync(featurePipeline, ResponseMap, request)` helper for protocol-level tests
- [ ] Provides version-specific engine factory methods (create H10/H11/H2/H3 engine with builder options)
- [ ] Inherits from `EngineTestBase` to reuse existing helpers
- [ ] Build passes

---

### TASK-001-017: Archive IntegrationTests + Mark Obsolete
**Description:** As a maintainer, I want all 84 integration test classes marked `[Obsolete]` and the IntegrationTests project removed from the default CI `dotnet test` target.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-001-004 through TASK-001-015 (all migration tasks complete)
**Successors:** none
**Parallel:** no — final step

**Acceptance Criteria:**
- [ ] All 84 integration test classes carry `[Obsolete("Replaced by StreamTests.Acceptance.{Protocol}.{ClassName}")]`
- [ ] IntegrationTests project removed from default CI test command (but kept in solution file)
- [ ] CI pipeline updated if applicable (check `.github/workflows/` or equivalent)
- [ ] Full `dotnet test` on StreamTests and Tests projects passes
- [ ] Zero flaky tests in the new suite

---

## Task Dependency Graph

```
TASK-001-001 (ScriptedFake) ──┬──→ TASK-001-016 (AcceptanceTestBase) ──┬──→ TASK-001-004 (H10 byte) ──→ TASK-001-008 (H10 feature) ──┐
                               │                                        ├──→ TASK-001-005 (H11 byte) ──→ TASK-001-009 (H11 feature) ──┤
TASK-001-002 (ResponseMap) ───┤                                        ├──→ TASK-001-012 (TLS all)   ──→ TASK-001-014 (Proxy)       ──┤
                               │                                        │                                                              │
TASK-001-003 (H2/H3 Builder) ┤                                        ├──→ TASK-001-006 (H2 byte)  ──→ TASK-001-010 (H2 feature)  ──┤
                               │                                        └──→ TASK-001-007 (H3 byte)  ──→ TASK-001-011 (H3 feature)  ──┤
                               │                                                                                                       │
                               └──→ TASK-001-013 (FakeProxy) ──────────────────────────────────────────→ TASK-001-014 (Proxy)       ──┤
                                                                                                                                       │
TASK-001-015 (LoggingBridge) ─────────────────────────────────────────────────────────────────────────────────────────────────────────┤
                                                                                                                                       │
                                                                                                                                       └──→ TASK-001-017 (Archive)
```

| Task | Title | Estimate | Predecessors | Parallel | Model |
|------|-------|----------|--------------|----------|-------|
| TASK-001-001 | ScriptedFakeConnectionStage | ~60k | none | yes (with 002, 003) | opus |
| TASK-001-002 | ResponseMap + ResponseMapFake | ~80k | none | yes (with 001, 003) | opus |
| TASK-001-003 | H2/H3 ResponseBuilder | ~70k | none | yes (with 001, 002) | opus |
| TASK-001-016 | AcceptanceTestBase | ~40k | 001, 002 | no | opus |
| TASK-001-004 | H10 byte-level tests (9 files) | ~150k | 016 | yes (with 005, 006, 007) | opus |
| TASK-001-005 | H11 byte-level tests (9 files) | ~150k | 016 | yes (with 004, 006, 007) | opus |
| TASK-001-006 | H2 frame-level tests (10 files) | ~180k | 016, 003 | yes (with 004, 005, 007) | opus |
| TASK-001-007 | H3 frame-level tests (10 files) | ~180k | 016, 003 | yes (with 004, 005, 006) | opus |
| TASK-001-008 | H10 feature-logic tests (6 files) | ~120k | 002, 004 | yes (with 009-012) | — |
| TASK-001-009 | H11 feature-logic tests (8 files) | ~150k | 002, 005 | yes (with 008, 010-012) | — |
| TASK-001-010 | H2 feature-logic tests (7 files) | ~140k | 002, 006 | yes (with 008, 009, 011, 012) | — |
| TASK-001-011 | H3 feature-logic tests (7 files) | ~140k | 002, 007 | yes (with 008-010, 012) | — |
| TASK-001-012 | TLS all tests (15 files) | ~200k | 001, 002 | yes (with 008-011) | opus |
| TASK-001-013 | FakeProxyStage | ~50k | 001 | yes (with 004-012) | — |
| TASK-001-014 | Proxy tests (2 files) | ~80k | 013, 012 | no | opus |
| TASK-001-015 | LoggingBridge → UnitTests | ~20k | none | yes (with any) | — |
| TASK-001-017 | Archive IntegrationTests | ~30k | 004-015 | no | — |

**Total estimated tokens:** ~1,840k (~1.8M)

## Functional Requirements

- FR-1: Every `[Fact]` and `[Theory]` method in IntegrationTests must have a corresponding test method in StreamTests/Acceptance or Tests with identical assertions
- FR-2: Stream tests must be fully deterministic — no network calls, no OS port allocation, no real TCP/QUIC/TLS
- FR-3: Byte-level fakes (`ScriptedFakeConnectionStage`) must support multi-response sequences, conditional responses, and error injection
- FR-4: Protocol-level fakes (`ResponseMapFake`) must support dynamic response generation based on request properties (path, headers, cookies)
- FR-5: `H2ResponseBuilder` must produce valid HTTP/2 frames decodable by existing `Protocol.Http2.FrameDecoder`
- FR-6: `H3ResponseBuilder` must produce valid HTTP/3 frames decodable by existing `Protocol.Http3.FrameDecoder`
- FR-7: `FakeProxyStage` must simulate CONNECT tunnel handshake at transport level
- FR-8: TLS acceptance tests must use `CertificateValidation` callback injection, not real TLS negotiation
- FR-9: All acceptance test files must follow existing test conventions: `sealed` class, `Spec` suffix, BDD method names, `[Fact(Timeout = 5000)]`, max 500 lines
- FR-10: `LoggingBridgeSpec` must be a pure unit test with no server dependencies
- FR-11: All 84 integration test classes must be marked `[Obsolete]` after migration
- FR-12: IntegrationTests project must be removed from default CI test execution

## Non-Goals

- Deleting integration test code (archived, not deleted)
- Changing existing StreamTests or UnitTests
- Adding new test scenarios beyond what IntegrationTests cover
- Modifying production code (TurboHTTP library itself)
- Achieving code coverage targets beyond 1:1 parity
- Performance benchmarking of test execution speed

## Technical Considerations

- **`ScriptedFakeConnectionStage`** extends `GraphStage<FlowShape<IOutputItem, IInputItem>>` — same shape as `EngineFakeConnectionStage` but with `Func<int, byte[], byte[]>` instead of `Func<byte[]>`. Must handle `ConnectItem` for connection lifecycle.
- **`ResponseMapFake`** is a `BidiFlow` that sits where the engine+connection would normally be. It must process `HttpRequestMessage` → `HttpResponseMessage` without any transport or serialization.
- **H2/H3 builders** must correctly encode HPACK/QPACK headers. Reuse existing `HpackEncoder`/`QpackEncoder` from Protocol layer if possible.
- **TLS fakes** focus on `CertificateValidation` callback + `DangerousAcceptAnyServerCertificate` option injection. Pattern exists in `Http30CertificateValidationSpec`.
- **Port naming convention** applies to any new `GraphStage` inlets/outlets — use `StageName.Direction` format per CLAUDE.md.
- **Max 500 lines per test file** — split large integration test classes if needed during migration.
- **ARCHITECTURE.md** should be updated after this feature to reflect the new `StreamTests/Acceptance/` tier in the Testing Structure section.

## Success Metrics

- Zero flaky tests in the new StreamTests/Acceptance suite
- 84 integration test files → 83 acceptance StreamTest files + 1 UnitTest file
- All integration test methods have 1:1 stream test equivalents
- IntegrationTests project no longer runs in default CI
- StreamTests execution time significantly faster than IntegrationTests (no server startup, no network)

## Open Questions

*None — all design decisions were resolved during the brainstorming phase. See approved design spec at `docs/superpowers/specs/2026-04-16-integration-test-migration-design.md`.*
