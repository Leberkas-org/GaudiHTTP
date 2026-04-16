# Integration Test Migration — Design Spec

## Goal

Eliminate flaky IntegrationTests by migrating all ~87 test files to deterministic StreamTests (acceptance tier) and UnitTests. 1:1 coverage parity — every integration test method gets a stream test equivalent with the same protocol variant, same scenarios, same assertions.

## Motivation

The IntegrationTests hit a real Kestrel server over real TCP/QUIC. Any test can flake due to network timing, port conflicts, or OS-level transport behavior. The flakiness is spread across all categories — the real server is the root cause.

## Migration Strategy: Bottom-Up Per Feature

Migrate one feature category at a time, ordered by dependency. Each phase produces one or more PRs. Integration test files stay untouched until their StreamTest replacement is merged.

### Phase Order

| Phase | Category | Files | Depends On |
|-------|----------|:-----:|------------|
| 1 | Smoke (GET/POST/status/headers) | 6 | — |
| 2 | Connection (keep-alive, close, reuse) | 5 | Smoke |
| 3 | Compression (gzip/deflate/br response) | 5 | Smoke |
| 4 | Request Compression (gzip/deflate/br request body) | 5 | Smoke |
| 5 | Cookies (set/echo/domain/path/expiry) | 5 | Smoke |
| 6 | Redirects (301-308, chains, loops, cross-origin, security) | 7 | Smoke |
| 7 | Retry (408/429/503, succeed-after-N) | 5 | Smoke |
| 8 | Cache (max-age, etag, last-modified, vary, no-store) | 5 | Smoke |
| 9 | Expect-Continue (100-continue flow) | 5 | Smoke |
| 10 | Edge Cases (large headers, unknown encoding, empty body) | 5 | Smoke |
| 11 | Error Handling (abort, truncated, corrupt) | 5 | Smoke |
| 12 | Resilience (slow headers/body, content-length mismatch) | 5 | Smoke |
| 13 | Concurrency (parallel, burst, mixed methods) | 5 | Smoke |
| 14 | Options (auth, drain, conn-id) | 5 | Smoke + Connection |
| 15 | Handler Pipeline (custom DelegatingHandler) | 4 | Smoke |
| 16 | Feature Interactions (cookie+redirect, cache+gzip, redirect+retry) | 5 | Phases 3-8 |
| 17 | Proxy (CONNECT tunnel, relay) | 2 | Smoke + TLS |

Total: ~87 integration test files → ~86 acceptance StreamTests + 1 UnitTest (LoggingBridgeSpec).

## Test Infrastructure

### New Pieces (all in `StreamTests/Acceptance/Shared/`)

#### 1. `ScriptedFakeConnectionStage`

Extends the existing `EngineFakeConnectionStage` pattern. Accepts `Func<int, byte[], byte[]>` (request index + outbound bytes → response bytes) instead of a simple `Func<byte[]>`.

Supports:
- Multi-response sequences (connection reuse tests)
- Conditional responses (retry succeed-after-N with counter)
- Error injection (truncated body, abort mid-stream, corrupt bytes)

Used by: byte-level tests (phases 1-4, 9-13) across all protocols.

#### 2. `ResponseMap` + `ResponseMapFake`

Protocol-level fake that maps `HttpRequestMessage → HttpResponseMessage`. No byte crafting.

`ResponseMap` is a builder:
```csharp
var responses = new ResponseMap()
    .On("/hello", HttpStatusCode.OK, "Hello World")
    .On("/redirect/302/hello", HttpStatusCode.Found, headers: h => h.Location = "/hello")
    .On("/cookie/echo", req => BuildCookieEchoResponse(req));
```

`ResponseMapFake` is a `BidiFlow<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage, NotUsed>` that applies the map. Sits where the engine+connection would normally be.

Used by: feature logic tests (phases 5-8, 14-16) where wire format is not the concern.

#### 3. `H2ResponseBuilder` / `H3ResponseBuilder`

Fluent helpers for constructing frame-level byte arrays:
```csharp
var frames = H2ResponseBuilder.Create()
    .Settings(maxConcurrent: 100)
    .Headers(streamId: 1, status: 200, headers: ("content-length", "5"))
    .Data(streamId: 1, "hello", endStream: true)
    .Build();
```

Reduces byte-crafting friction for H2/H3 acceptance tests.

#### 4. `FakeProxyStage`

Simulates CONNECT tunnel at the transport level. Intercepts `ConnectItem`, responds with `200 Connection Established` bytes, then passes through to an inner byte-level fake.

Used by: phase 17 (Proxy tests).

### Existing Infrastructure (reused as-is)

- `EngineTestBase` — base class with `SendAsync`, `SendH2EngineAsync`, `SendH3EngineAsync`
- `EngineFakeConnectionStage` — simple byte-level H1 fake
- `H2EngineFakeConnectionStage` — H2 frame-level fake
- `H3EngineFakeConnectionStage` — H3 frame-level fake
- `StreamTestBase` — TestKit base with materializer

## Folder Structure

```
StreamTests/
  Acceptance/                    # NEW — all migrated integration tests
    H10/
      SmokeSpec.cs
      ConnectionSpec.cs
      CompressionSpec.cs
      RequestCompressionSpec.cs
      CookieSpec.cs
      RedirectSpec.cs
      RetrySpec.cs
      CacheSpec.cs
      ExpectContinueSpec.cs
      EdgeCaseSpec.cs
      ErrorHandlingSpec.cs
      ResilienceSpec.cs
      ConcurrencySpec.cs
      OptionsSpec.cs
      FeatureInteractionSpec.cs
    H11/
      SmokeSpec.cs
      ConnectionSpec.cs
      CompressionSpec.cs
      RequestCompressionSpec.cs
      CookieSpec.cs
      RedirectSpec.cs
      RedirectSecuritySpec.cs
      RetrySpec.cs
      CacheSpec.cs
      ExpectContinueSpec.cs
      EdgeCaseSpec.cs
      ErrorHandlingSpec.cs
      ResilienceSpec.cs
      ConcurrencySpec.cs
      OptionsSpec.cs
      HandlerPipelineSpec.cs
      FeatureInteractionSpec.cs
    H2/
      SmokeSpec.cs
      ConnectionSpec.cs
      CompressionSpec.cs
      RequestCompressionSpec.cs
      CookieSpec.cs
      RedirectSpec.cs
      RetrySpec.cs
      CacheSpec.cs
      ExpectContinueSpec.cs
      EdgeCaseSpec.cs
      ErrorHandlingSpec.cs
      ResilienceSpec.cs
      ConcurrencySpec.cs
      MaxConcurrentStreamsSpec.cs
      OptionsSpec.cs
      HandlerPipelineSpec.cs
      FeatureInteractionSpec.cs
    H3/
      SmokeSpec.cs
      ConnectionSpec.cs
      CompressionSpec.cs
      RequestCompressionSpec.cs
      CookieSpec.cs
      RedirectSpec.cs
      RetrySpec.cs
      CacheSpec.cs
      ExpectContinueSpec.cs
      EdgeCaseSpec.cs
      ErrorHandlingSpec.cs
      ResilienceSpec.cs
      ConcurrencySpec.cs
      MaxStreamConcurrencySpec.cs
      OptionsSpec.cs
      HandlerPipelineSpec.cs
      FeatureInteractionSpec.cs
    TLS/
      SmokeSpec.cs
      ConnectionSpec.cs
      CompressionSpec.cs
      RequestCompressionSpec.cs
      CookieSpec.cs
      RedirectSpec.cs
      RedirectSecuritySpec.cs
      RetrySpec.cs
      CacheSpec.cs
      ExpectContinueSpec.cs
      ErrorHandlingSpec.cs
      ResilienceSpec.cs
      IntegrationSpec.cs
      OptionsSpec.cs
      FeatureInteractionTlsSpec.cs
    Proxy/
      ProxyConnectSpec.cs
      ProxyRelaySpec.cs
    Shared/
      ScriptedFakeConnectionStage.cs
      ResponseMap.cs
      ResponseMapFake.cs
      H2ResponseBuilder.cs
      H3ResponseBuilder.cs
      FakeProxyStage.cs
  # existing folders unchanged:
  Http10/
  Http11/
  Http2/
  Http3/
  Caching/
  Cookies/
  Semantics/
  Streams/
  Transport/
```

## Fake Tier Per Protocol

| Protocol | Smoke/Connection/Compression/EdgeCase/Error/Resilience/Concurrency | Cookies/Redirect/Retry/Cache/Interactions |
|----------|:--:|:--:|
| H10 | Byte-level (`ScriptedFakeConnectionStage`) | Protocol-level (`ResponseMapFake`) |
| H11 | Byte-level (`ScriptedFakeConnectionStage`) | Protocol-level (`ResponseMapFake`) |
| H2 | Frame-level (`H2EngineFakeConnectionStage` + `H2ResponseBuilder`) | Protocol-level (`ResponseMapFake`) |
| H3 | Frame-level (`H3EngineFakeConnectionStage` + `H3ResponseBuilder`) | Protocol-level (`ResponseMapFake`) |
| TLS | Byte-level + certificate validator injection | Protocol-level (`ResponseMapFake`) |
| Proxy | `FakeProxyStage` wrapping inner protocol fake | — |

## Migration Process Per File

1. **Analyze** — list every `[Fact]`/`[Theory]`, identify composed features, identify server endpoints used
2. **Choose fake tier** — byte-level if wire format matters, protocol-level if feature logic matters, mixed if both
3. **Write stream test** — same class name, same method names, same assertions. Replace `ClientHelper` with pipeline + fake.
4. **Verify** — new stream test green, old integration test still green, assertion parity confirmed
5. **Mark done** — add `[Obsolete("Replaced by StreamTests.Acceptance.{Protocol}.{ClassName}")]` to integration test class

## Completion Criteria

- All ~86 acceptance stream test files exist in `StreamTests/Acceptance/` and are green
- `LoggingBridgeSpec` migrated to UnitTests
- All ~87 integration test classes carry `[Obsolete]` attribute
- IntegrationTests project removed from default CI `dotnet test` target (kept in solution for reference)
- Zero flaky tests in the new suite (no network, no timing dependencies)

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Byte-level fakes miss real protocol bugs | Engine-level StreamTests already validate wire format against RFC specs. Real-world bugs caught via targeted byte-level unit tests. |
| TLS tests hard to fake | Focus on `CertificateValidation` callback + `DangerousAcceptAnyServerCertificate` option injection. Actual TLS negotiation is .NET runtime, not TurboHTTP code. Pattern exists in `Http30CertificateValidationSpec`. |
| Proxy tests need real proxy simulation | `FakeProxyStage` simulates CONNECT handshake at transport level only. Real proxy TCP behavior is not under test. |
| H2/H3 byte-crafting is verbose | `H2ResponseBuilder` / `H3ResponseBuilder` fluent helpers invested once, reused across all tests. |

## Out of Scope

- Deleting integration test code (archived per decision)
- Changing existing StreamTests or UnitTests
- Adding new test scenarios beyond what IntegrationTests cover
