# Analysis: 100% RFC Integration Test Coverage

## Current State

| Layer | Tests | Coverage |
|-------|-------|----------|
| Unit tests (TurboHttp.Tests) | 1,827 | All RFC sections |
| Stream tests (TurboHttp.StreamTests) | 487 | All stages + IO actors |
| Integration tests (TurboHttp.IntegrationTests) | **0** | Infrastructure only |

**Infrastructure ready:**
- 3 Kestrel fixtures: `KestrelFixture` (HTTP/1.x), `KestrelH2Fixture` (HTTP/2 h2c), `KestrelTlsFixture` (HTTPS)
- 120+ routes registered across all fixtures
- `TestKit` base class with Akka ActorSystem + `ClientManager` actor
- Empty placeholder folders: `Http10/`, `Http11/`, `Http20/`

**Key question:** What does "100% RFC coverage with integration tests" mean?

Unit tests verify **encoding/decoding correctness** in isolation.
Stream tests verify **stage wiring and data flow** with fake transports.
Integration tests verify **end-to-end behavior through real TCP/TLS against real HTTP servers**.

The goal is not to re-test what unit/stream tests already cover, but to validate **cross-cutting behaviors that only emerge when all layers interact over real network connections**.

---

## What Integration Tests Add That Unit/Stream Tests Cannot

### 1. Real TCP Transport
- Actual TCP connection establishment, reuse, and teardown
- Real network buffering, Nagle's algorithm, TCP window effects
- TLS handshake, certificate validation, protocol negotiation

### 2. Real HTTP Server Semantics
- Kestrel's actual response generation (correct headers, chunking, etc.)
- Server-driven content negotiation
- Real timing for caching (Age, Expires, Date headers)
- Actual connection lifecycle (keep-alive timeout, max requests)

### 3. Full Pipeline Integration
- Request enrichment → cookie injection → cache lookup → engine routing → encoding → TCP → decoding → decompression → cookie storage → cache storage → retry → redirect → response delivery
- Feedback loops: retry re-enters at cookie injection, redirect re-enters at request enrichment
- Cache hit bypasses engine entirely

### 4. Actor + Stream + Channel Coordination
- `PoolRouterActor` → `HostPoolActor` → `ConnectionActor` lifecycle
- `ConnectionHandle` delivery from actor system to `ConnectionStage`
- `ClientByteMover` async pump tasks with real TCP streams
- Connection state tracking (Active → Idle → Reusable → Closed)

---

## RFC-by-RFC Integration Test Matrix

### RFC 1945 — HTTP/1.0 (Fixture: `KestrelFixture`)

| Section | What to Test | Routes Available | Priority |
|---------|-------------|------------------|----------|
| §4 (Message Format) | GET request produces valid HTTP/1.0 wire format, response parsed correctly | `/hello`, `/ping` | High |
| §5 (Request) | All methods work end-to-end (GET, HEAD, POST) | `/any`, `/echo` | High |
| §6 (Response) | Status codes 200, 204, 301, 302, 400, 404, 500 parsed correctly | `/status/{code}` | High |
| §7 (Headers) | Custom headers round-trip, multi-value headers | `/headers/echo`, `/headers/set`, `/multiheader` | Medium |
| §8 (Body) | Body transmission for various sizes, empty bodies | `/large/{kb}`, `/empty-cl`, `/echo` | High |
| §10 (Connection) | Connection closes after each request (no keep-alive) | `/close`, `/hello` (sequential) | High |

**Estimated tests: 15–20**
**Key insight:** HTTP/1.0 tests must verify that connections are NOT reused (no persistent connections in 1.0).

---

### RFC 9112 — HTTP/1.1 (Fixture: `KestrelFixture`)

| Section | What to Test | Routes Available | Priority |
|---------|-------------|------------------|----------|
| §2.1 (Message Format) | Request/response round-trip with Host header | `/hello`, `/ping` | High |
| §3 (Request Line) | Various methods, URI encoding | `/any`, `/methods` | Medium |
| §4 (Status Line) | Status code range, reason phrases | `/status/{code}` | Medium |
| §6 (Body) | Content-Length bodies, empty bodies | `/large/{kb}`, `/echo`, `/empty-cl` | High |
| §7 (Chunked) | Chunked transfer encoding decode | `/chunked/{kb}`, `/chunked/exact/{n}/{s}` | **Critical** |
| §7.1 (Chunk Extensions) | Chunked with trailers | `/chunked/trailer` | Medium |
| §9 (Connection Mgmt) | Keep-alive reuse, Connection: close | `/conn/keep-alive`, `/conn/close`, `/conn/default` | **Critical** |
| §9 (Pipelining) | Sequential requests on same connection | `/hello` × N | High |

**Estimated tests: 25–30**
**Key insight:** Chunked transfer and connection reuse are the crown jewels — they can only be truly validated end-to-end.

---

### RFC 9113 — HTTP/2 (Fixture: `KestrelH2Fixture`)

| Section | What to Test | Routes Available | Priority |
|---------|-------------|------------------|----------|
| §3.4 (Connection Preface) | Client preface sent, server SETTINGS received | `/hello` (first request) | **Critical** |
| §4 (Frame Layer) | Frames parsed correctly over real TCP | `/hello`, `/large/{kb}` | High |
| §5 (Streams) | Stream multiplexing — concurrent requests | `/hello`, `/slow/{count}` | **Critical** |
| §5.1 (Stream States) | Stream lifecycle (open → half-closed → closed) | `/hello`, `/echo` | High |
| §6.1 (DATA) | Large payloads with flow control | `/large/{kb}`, `/h2/echo-binary` | High |
| §6.2 (HEADERS) | HPACK-encoded headers round-trip | `/h2/many-headers`, `/headers/echo` | High |
| §6.5 (SETTINGS) | Initial SETTINGS exchange | `/h2/settings` | High |
| §6.7 (PING) | PING keep-alive (if client sends) | `/slow/{count}` (long-lived) | Low |
| §6.8 (GOAWAY) | Graceful connection shutdown | `/h2/abort` | Medium |
| §8 (HTTP Semantics) | Pseudo-headers (:method, :path, :scheme, :authority) | `/h2/echo-path` | High |

**Estimated tests: 25–30**
**Key insight:** Stream multiplexing is impossible to test without real TCP. Send multiple concurrent requests and verify all return correctly.

---

### RFC 7541 — HPACK (Tested implicitly via HTTP/2)

No separate integration tests needed. HPACK is exercised in every HTTP/2 request.
Verify indirectly by:
- Sending requests with many headers → `/h2/many-headers`
- Large header blocks → `/h2/large-headers/{kb}`
- Sensitive headers (Authorization, Cookie) → `/auth`, `/cookie/*`

**Estimated dedicated tests: 0** (covered by HTTP/2 tests)

---

### RFC 9110 — HTTP Semantics (Fixtures: all three)

#### §8.4 Content Encoding

| What to Test | Routes Available | Priority |
|-------------|------------------|----------|
| gzip decompression end-to-end | `/compress/gzip/{kb}` | **Critical** |
| deflate decompression | `/compress/deflate/{kb}` | High |
| brotli decompression | `/compress/br/{kb}` | High |
| identity (no compression) | `/compress/identity/{kb}` | Medium |
| Accept-Encoding negotiation | `/compress/negotiate` | High |

**Estimated tests: 8–10**

#### §9.2 Retry Logic

| What to Test | Routes Available | Priority |
|-------------|------------------|----------|
| Retry on 408 Request Timeout | `/retry/408` | High |
| Retry on 503 Service Unavailable | `/retry/503` | **Critical** |
| Retry-After header (seconds) | `/retry/503-retry-after/{s}` | High |
| Retry-After header (HTTP-date) | `/retry/503-retry-after-date` | Medium |
| Succeed after N attempts | `/retry/succeed-after/{n}` | **Critical** |
| Idempotent PUT/DELETE retried | `/retry/503` (PUT/DELETE) | High |
| Non-idempotent POST NOT retried | `/retry/non-idempotent-503` | **Critical** |

**Estimated tests: 10–12**

#### §15.4 Redirects

| What to Test | Routes Available | Priority |
|-------------|------------------|----------|
| 301 Moved Permanently | `/redirect/301/hello` | High |
| 302 Found (POST → GET rewrite) | `/redirect/302` | **Critical** |
| 303 See Other (forces GET) | `/redirect/303` | High |
| 307 Temporary (preserves method+body) | `/redirect/307` | **Critical** |
| 308 Permanent (preserves method+body) | `/redirect/308` | **Critical** |
| Redirect chain (N hops) | `/redirect/chain/{n}` | High |
| Redirect loop detection | `/redirect/loop` | **Critical** |
| Relative Location | `/redirect/relative` | Medium |
| Cross-origin redirect | `/redirect/cross-origin` | High |
| Cross-origin strips Authorization | `/redirect/cross-origin-auth` | **Critical** |
| HTTPS → HTTP downgrade blocked | `/redirect/cross-scheme` | **Critical** (TLS fixture) |

**Estimated tests: 15–18**

#### §12.5 Content Negotiation

| What to Test | Routes Available | Priority |
|-------------|------------------|----------|
| Accept: application/json | `/negotiate` | Medium |
| Accept: text/html | `/negotiate` | Medium |
| Vary header respected | `/negotiate/vary` | Medium |

**Estimated tests: 3–4**

---

### RFC 6265 — Cookies (Fixtures: `KestrelFixture`, `KestrelTlsFixture`)

| What to Test | Routes Available | Priority |
|-------------|------------------|----------|
| Set-Cookie → Cookie round-trip | `/cookie/set/{n}/{v}` + `/cookie/echo` | **Critical** |
| Secure flag (only sent over HTTPS) | `/cookie/set-secure/{n}/{v}` | **Critical** (TLS) |
| HttpOnly flag stored correctly | `/cookie/set-httponly/{n}/{v}` | High |
| SameSite attribute | `/cookie/set-samesite/{n}/{v}/{p}` | Medium |
| Domain matching | `/cookie/set-domain/{n}/{v}/{d}` | High |
| Path matching | `/cookie/set-path/{n}/{v}/{path}` | High |
| Max-Age / Expires eviction | `/cookie/set-expires/{n}/{v}/{s}` | High |
| Cookie deletion (Max-Age=0) | `/cookie/delete/{name}` | Medium |
| Multiple Set-Cookie headers | `/cookie/set-multiple` | High |
| Cookies survive redirects | `/cookie/set-and-redirect` | **Critical** |

**Estimated tests: 15–18**
**Key insight:** Cookie + redirect interaction is a real-world critical path that ONLY integration tests can validate.

---

### RFC 9111 — Caching (Fixture: `KestrelFixture`)

| What to Test | Routes Available | Priority |
|-------------|------------------|----------|
| Cache-Control: max-age hit | `/cache/max-age/{s}` (2 requests) | **Critical** |
| Cache-Control: no-cache (always revalidate) | `/cache/no-cache` | High |
| Cache-Control: no-store (never cache) | `/cache/no-store` | High |
| ETag + If-None-Match → 304 | `/cache/etag/{id}` | **Critical** |
| Last-Modified + If-Modified-Since → 304 | `/cache/last-modified/{id}` | **Critical** |
| Vary header (different cache per header value) | `/cache/vary/{header}` | High |
| must-revalidate | `/cache/must-revalidate` | High |
| s-maxage | `/cache/s-maxage/{s}` | Medium |
| Expires header | `/cache/expires` | Medium |
| Cache-Control: private | `/cache/private` | Medium |

**Estimated tests: 15–18**
**Key insight:** Caching tests must send request 1, then request 2, and verify that request 2 is served from cache (no network) or triggers revalidation (conditional request). This requires real timing.

---

### RFC 9112 §9 — Connection Reuse (Fixture: `KestrelFixture`)

| What to Test | Routes Available | Priority |
|-------------|------------------|----------|
| HTTP/1.1 default keep-alive | `/conn/default` (2 requests, same TCP) | **Critical** |
| Connection: close respected | `/conn/close` | High |
| HTTP/1.0 Connection: Keep-Alive opt-in | `/conn/keep-alive` | High |
| Per-host connection pooling | `/hello` (multiple hosts) | **Critical** |
| Idle connection eviction | `/hello` + delay + `/hello` | Medium |

**Estimated tests: 8–10**

---

## Proposed Test File Structure

```
TurboHttp.IntegrationTests/
├── Shared/
│   ├── TestKit.cs                          (existing)
│   ├── KestrelFixture.cs                   (existing)
│   ├── KestrelH2Fixture.cs                 (existing)
│   ├── KestrelTlsFixture.cs               (existing)
│   └── Routes.cs                           (existing)
│
├── Http10/
│   └── 01_Http10BasicTests.cs              (15-20 tests)
│       RFC 1945: methods, status codes, headers, body, connection close
│
├── Http11/
│   ├── 01_Http11BasicTests.cs              (10-12 tests)
│   │   RFC 9112 §2-6: request/response, Host header, bodies
│   ├── 02_Http11ChunkedTests.cs            (8-10 tests)
│   │   RFC 9112 §7: chunked transfer encoding, trailers
│   ├── 03_Http11ConnectionTests.cs         (8-10 tests)
│   │   RFC 9112 §9: keep-alive, close, pipelining, pooling
│   ├── 04_Http11RedirectTests.cs           (15-18 tests)
│   │   RFC 9110 §15.4: 301/302/303/307/308, chains, loops, cross-origin
│   ├── 05_Http11RetryTests.cs              (10-12 tests)
│   │   RFC 9110 §9.2: 408/503 retry, Retry-After, idempotency
│   ├── 06_Http11CookieTests.cs             (15-18 tests)
│   │   RFC 6265: set/read/domain/path/secure/httponly/samesite/expiry
│   ├── 07_Http11CacheTests.cs              (15-18 tests)
│   │   RFC 9111: max-age, no-cache, ETag/304, Last-Modified, Vary
│   ├── 08_Http11ContentEncodingTests.cs    (8-10 tests)
│   │   RFC 9110 §8.4: gzip/deflate/brotli decompression
│   └── 09_Http11ContentNegotiationTests.cs (3-4 tests)
│       RFC 9110 §12.5: Accept header, Vary
│
├── Http20/
│   ├── 01_Http20BasicTests.cs              (10-12 tests)
│   │   RFC 9113 §3-4: connection preface, SETTINGS, basic request/response
│   ├── 02_Http20MultiplexingTests.cs       (8-10 tests)
│   │   RFC 9113 §5: concurrent streams, stream lifecycle
│   ├── 03_Http20HeaderTests.cs             (6-8 tests)
│   │   RFC 9113 §6.2 + RFC 7541: HPACK round-trip, many headers, large headers
│   ├── 04_Http20DataTests.cs               (6-8 tests)
│   │   RFC 9113 §6.1: large payloads, flow control
│   ├── 05_Http20RedirectTests.cs           (8-10 tests)
│   │   RFC 9110 §15.4 over HTTP/2
│   ├── 06_Http20CookieTests.cs             (6-8 tests)
│   │   RFC 6265 over HTTP/2
│   └── 07_Http20CacheTests.cs              (6-8 tests)
│       RFC 9111 over HTTP/2
│
├── Tls/
│   ├── 01_TlsBasicTests.cs                (8-10 tests)
│   │   HTTPS handshake, basic request/response over TLS
│   ├── 02_TlsRedirectTests.cs             (5-6 tests)
│   │   HTTPS→HTTP downgrade protection, cross-scheme redirects
│   └── 03_TlsCookieSecureTests.cs         (5-6 tests)
│       Secure cookie flag enforcement over HTTPS
│
└── Pipeline/
    ├── 01_FullPipelineTests.cs             (8-10 tests)
    │   End-to-end: request → all stages → TCP → all stages → response
    └── 02_FeedbackLoopTests.cs             (6-8 tests)
        Redirect + retry feedback loops, cache hit bypass
```

---

## Test Counts Summary

| Area | RFC | Est. Tests |
|------|-----|-----------|
| HTTP/1.0 basics | RFC 1945 | 15–20 |
| HTTP/1.1 basics | RFC 9112 §2-6 | 10–12 |
| Chunked transfer | RFC 9112 §7 | 8–10 |
| Connection management | RFC 9112 §9 | 8–10 |
| Redirects (HTTP/1.1) | RFC 9110 §15.4 | 15–18 |
| Retry (HTTP/1.1) | RFC 9110 §9.2 | 10–12 |
| Cookies (HTTP/1.1) | RFC 6265 | 15–18 |
| Caching (HTTP/1.1) | RFC 9111 | 15–18 |
| Content encoding | RFC 9110 §8.4 | 8–10 |
| Content negotiation | RFC 9110 §12.5 | 3–4 |
| HTTP/2 basics | RFC 9113 §3-5 | 18–22 |
| HTTP/2 headers + HPACK | RFC 9113 §6 + RFC 7541 | 6–8 |
| HTTP/2 data + flow ctrl | RFC 9113 §6.1 | 6–8 |
| HTTP/2 cross-cutting | RFC 9110/6265/9111 over H2 | 20–26 |
| TLS | HTTPS-specific | 18–22 |
| Full pipeline | Cross-cutting | 14–18 |
| **TOTAL** | | **~190–235** |

---

## Implementation Strategy

### Phase 1: Foundation (TASK-001 to TASK-003)
**Goal:** Prove the test harness works end-to-end.

1. **TASK-001:** HTTP/1.1 basic tests — simplest possible: send GET `/hello`, assert 200 + body.
   Validates: fixture startup, `ITurboHttpClient` creation, actor system, connection pooling, encode/decode, full pipeline.

2. **TASK-002:** HTTP/2 basic tests — send GET `/hello` over h2c, assert 200 + body.
   Validates: HTTP/2 preface, SETTINGS exchange, HPACK, stream lifecycle.

3. **TASK-003:** TLS basic tests — send GET `/hello` over HTTPS, assert 200 + body.
   Validates: TLS handshake, certificate handling, encrypted transport.

### Phase 2: Protocol Core (TASK-004 to TASK-007)
**Goal:** Cover the protocol mechanics that unit tests can't fully validate.

4. **TASK-004:** HTTP/1.1 chunked transfer — various sizes, trailers
5. **TASK-005:** HTTP/1.1 connection management — keep-alive, close, pooling
6. **TASK-006:** HTTP/2 multiplexing — concurrent requests on single connection
7. **TASK-007:** HTTP/1.0 basics — verify connection-close behavior, no keep-alive

### Phase 3: Business Logic (TASK-008 to TASK-012)
**Goal:** Validate cross-cutting concerns that span multiple stages.

8. **TASK-008:** Redirects (HTTP/1.1 + HTTP/2) — all status codes, chains, loops, cross-origin
9. **TASK-009:** Cookies — set/read/domain/path/secure/redirect interaction
10. **TASK-010:** Caching — freshness, revalidation, 304 merge, Vary
11. **TASK-011:** Content encoding — gzip/deflate/brotli end-to-end
12. **TASK-012:** Retry — 408/503, Retry-After, idempotency rules

### Phase 4: Edge Cases + TLS (TASK-013 to TASK-015)
**Goal:** Cover security-sensitive and edge-case scenarios.

13. **TASK-013:** TLS redirect + cookie security — HTTPS→HTTP downgrade, Secure cookies
14. **TASK-014:** Full pipeline — request through all stages with cache + cookies + redirect combined
15. **TASK-015:** Content negotiation, range requests, edge cases (large headers, slow responses)

---

## Test Pattern Template

Each integration test class should follow this pattern:

```csharp
namespace TurboHttp.IntegrationTests.Http11;

public sealed class Http11BasicTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _server;

    public Http11BasicTests(KestrelFixture server)
    {
        _server = server;
    }

    [Fact(DisplayName = "INT-9112-2-BAS-001: GET /hello returns 200 with body")]
    public async Task BAS_001_Get_Hello_Returns_200()
    {
        // Arrange: create TurboHttpClient pointing at fixture
        await using var client = CreateClient(_server.Port, HttpVersion.Version11);

        // Act: send request through full pipeline
        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_server.Port}/hello"));

        // Assert: verify end-to-end
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content!.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }
}
```

**DisplayName convention:** `INT-{RFC}-{section}-{CAT}-{NNN}: description`
- `INT` prefix distinguishes from unit (`RFC`) and stream (`ST`) tests
- Categories: `BAS` (basic), `CHK` (chunked), `CON` (connection), `RDR` (redirect), `RTY` (retry), `CKE` (cookie), `CCH` (cache), `ENC` (encoding), `MUX` (multiplexing), `TLS` (TLS-specific), `PIP` (pipeline)

---

## Missing Route Gaps

Routes that should be added to fixtures for full coverage:

| Route | Purpose | Fixture |
|-------|---------|---------|
| `GET /http10` | Force HTTP/1.0 response (no chunked, connection close) | KestrelFixture |
| `GET /cache/stale-while-revalidate` | RFC 9111 §5.2.2.12 | All |
| `GET /cache/age/{seconds}` | Response with pre-set Age header | All |
| `POST /redirect/301` | 301 on POST (method rewrite to GET) | All |
| `GET /h2/goaway` | Trigger GOAWAY frame | KestrelH2Fixture |
| `GET /h2/rst-stream` | Trigger RST_STREAM on specific stream | KestrelH2Fixture |
| `GET /h2/window-update` | Trigger window exhaustion + update | KestrelH2Fixture |
| `GET /conn/max-requests/{n}` | Close after N requests | KestrelFixture |
| `GET /conn/idle-timeout/{ms}` | Close after idle timeout | KestrelFixture |

---

## Challenges and Mitigations

### 1. Timing-Dependent Tests
**Problem:** Cache freshness, Retry-After, idle eviction depend on wall-clock time.
**Mitigation:** Use short durations (1–2 seconds max). Use `/cache/max-age/1` not `/cache/max-age/3600`. Accept ±500ms tolerance in assertions.

### 2. Port Conflicts
**Problem:** Three fixtures each bind random ports.
**Mitigation:** Already handled — fixtures use `PortFinder.FindFreeLocalPort()`.

### 3. Connection Pool State Leaking Between Tests
**Problem:** If tests share a fixture, connection pool state from test A may affect test B.
**Mitigation:** Create a fresh `ITurboHttpClient` (with new actor system) per test via `TestKit` base class. Each test gets its own `ActorSystem` and connection pool.

### 4. HTTP/2 h2c Without Prior Knowledge
**Problem:** Kestrel's h2c fixture requires the client to speak HTTP/2 directly (no upgrade).
**Mitigation:** TurboHttp's `Http20Engine` already handles h2c with connection preface. Ensure `DefaultRequestVersion = HttpVersion.Version20`.

### 5. Self-Signed TLS Certificate
**Problem:** `KestrelTlsFixture` uses an in-memory self-signed cert.
**Mitigation:** TurboHttp client must be configured to trust it (custom `ServerCertificateCustomValidationCallback` or add cert to trust store in test setup).

---

## Summary

- **~200 integration tests** across 20 test files will provide full RFC coverage
- **Phase 1 (3 tasks)** proves the harness works — highest value, lowest effort
- **Phase 2-3 (9 tasks)** cover protocol mechanics and business logic — the bulk of the work
- **Phase 4 (3 tasks)** cover edge cases and security
- **No new routes needed** for Phase 1-3; ~9 additional routes needed for complete Phase 4
- **HPACK (RFC 7541) needs no dedicated tests** — fully exercised by HTTP/2 tests
- **Critical paths** that ONLY integration tests can validate:
  1. Connection pooling and reuse
  2. Stream multiplexing (HTTP/2)
  3. Cookie + redirect interaction
  4. Cache freshness with real timing
  5. TLS + Secure cookie enforcement
  6. Retry with stateful server (`succeed-after/{n}`)
