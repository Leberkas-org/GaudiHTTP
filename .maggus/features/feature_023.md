# Feature 023: Integration Test Depth — Interactions, Resilience, Request Compression & Handlers

## Introduction

Feature 021 bringt alle HTTP-Versionen auf gleiche **Breite** (jede Feature-Kategorie × jede Version). Feature 023 ergänzt die **Tiefe**: Wie verhalten sich BidiStages zusammen? Was passiert bei kaputten Antworten? Funktioniert Request-Compression end-to-end? Können Custom Handler die Pipeline erweitern?

Aktuell sind alle 8 BidiStages nur isoliert getestet. In der Production-Pipeline sind sie gestackt:
```
Tracing → Handler → Redirect → Cookie → Retry → ExpectContinue → Cache → ContentEncoding → Engine
```
Interaktionen zwischen Stages (z.B. Cookie + Redirect, Cache + Compression) sind nicht abgedeckt. Ebenso fehlen Tests für fehlerhafte Server-Antworten (Resilience), Request-Body-Kompression und die Handler-Pipeline.

### Architecture Context

- **Components involved:** `src/TurboHttp.IntegrationTests/` (test files), `Shared/Routes.cs` (server routes), `Shared/ClientHelper.cs` (client factory mit `configure` callback), `TurboHttpClientBuilderExtensions.cs` (Builder-API)
- **New patterns:** `ClientHelper.CreateClient(port, version, configure: builder => ...)` wird erstmals intensiv genutzt um RequestCompression, ExpectContinue und Custom Handler zu konfigurieren
- **Missing Builder API:** `WithRequestCompression(policy?)` und `WithExpectContinue(policy?)` fehlen als Extension Methods — müssen ergänzt werden
- **All 4 versions:** HTTP/1.0, HTTP/1.1, HTTP/2, TLS (per User-Entscheidung)

## Goals

- Verifizieren dass BidiStage-Interaktionen (6+ Kombinationen) über alle HTTP-Versionen korrekt funktionieren
- Sicherstellen dass der Client kaputte Server-Antworten graceful behandelt (8+ Resilience-Szenarien)
- End-to-end Validierung der Request-Body-Kompression (gzip/deflate/br) über alle Versionen
- Custom Handler Pipeline (UseRequest/UseResponse/AddHandler) end-to-end verifizieren
- `WithRequestCompression()` und `WithExpectContinue()` Builder-Extensions ergänzen

## Tasks

### TASK-023-001: Builder Extensions + Resilience Routes
**Description:** Als Entwickler möchte ich `WithRequestCompression()` / `WithExpectContinue()` Builder-Extensions und neue Server-Routen für Resilience-Tests, damit die nachfolgenden Tasks ihre Tests konfigurieren können.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-023-004, TASK-023-005, TASK-023-006, TASK-023-007
**Parallel:** yes — can run alongside TASK-023-002, TASK-023-003, TASK-023-008

**Builder Extensions (in `TurboHttpClientBuilderExtensions.cs`):**
```csharp
public static ITurboHttpClientBuilder WithRequestCompression(
    this ITurboHttpClientBuilder builder, RequestCompressionPolicy? policy = null)
{
    builder.Services.Configure<TurboClientDescriptor>(builder.Name,
        d => { d.RequestCompressionPolicy = policy ?? RequestCompressionPolicy.Default; });
    return builder;
}

public static ITurboHttpClientBuilder WithExpectContinue(
    this ITurboHttpClientBuilder builder, Expect100Policy? policy = null)
{
    builder.Services.Configure<TurboClientDescriptor>(builder.Name,
        d => { d.Expect100Policy = policy ?? Expect100Policy.Default; });
    return builder;
}
```

**Neue Routen in `Routes.cs` — `RegisterResilienceRoutes()`:**
- `GET /resilience/content-length-mismatch` — `Content-Length: 1000` but sends only 500 bytes, then closes
- `GET /resilience/corrupt-gzip` — `Content-Encoding: gzip` but body is random bytes
- `GET /resilience/corrupt-br` — `Content-Encoding: br` but body is random bytes
- `GET /resilience/truncated-body/{kb}` — sends `kb` KB header, stops at 50% of body
- `GET /resilience/slow-headers/{ms}` — delay `ms` before sending headers
- `GET /resilience/slow-body/{ms}` — sends first half of body, delays `ms`, then sends rest
- `GET /resilience/invalid-header` — response with header containing invalid characters
- `GET /resilience/empty-response` — closes connection immediately (no status line)

**Neue Routen — `RegisterRequestCompressionRoutes()`:**
- `POST /compress/echo` — reads request body, echoes back uncompressed + returns `X-Content-Encoding: {received Content-Encoding}` header to prove compression was received
- `POST /compress/verify-gzip` — verifies body is valid gzip, decompresses, echoes
- `POST /compress/verify-deflate` — verifies body is valid deflate
- `POST /compress/verify-br` — verifies body is valid brotli

**Acceptance Criteria:**
- [ ] `WithRequestCompression()` and `WithExpectContinue()` added to `TurboHttpClientBuilderExtensions.cs`
- [ ] `Routes.RegisterResilienceRoutes()` with 8 routes added
- [ ] `Routes.RegisterRequestCompressionRoutes()` with 4 routes added
- [ ] All fixtures call both new registration methods
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` passes with zero warnings
- [ ] Existing tests still pass

**Files:**
- `src/TurboHttp/TurboHttpClientBuilderExtensions.cs` — add 2 extension methods
- `src/TurboHttp.IntegrationTests/Shared/Routes.cs` — add 2 route groups (12 routes)
- `src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs` — call new registrations
- `src/TurboHttp.IntegrationTests/Shared/KestrelH2Fixture.cs` — call new registrations
- `src/TurboHttp.IntegrationTests/Shared/KestrelTlsFixture.cs` — call new registrations

---

### TASK-023-002: Feature Interaction Tests — HTTP/1.1 (Reference Implementation)
**Description:** Als Entwickler möchte ich Tests die das Zusammenspiel mehrerer BidiStages verifizieren, damit sichergestellt ist dass die Pipeline-Komposition korrekt funktioniert.

**Token Estimate:** ~45k tokens
**Predecessors:** none
**Successors:** TASK-023-003
**Parallel:** yes — can run alongside TASK-023-001, TASK-023-008

**Test-Szenarien (7 Tests):**

| # | DisplayName | Stages | Was wird geprüft |
|---|-------------|--------|-----------------|
| 1 | `Interaction-001: Redirect preserves cookies across hops` | Cookie + Redirect | Set-Cookie → 302 → Cookie wird an neues Ziel gesendet |
| 2 | `Interaction-002: Compressed response served from cache` | Cache + Compression | gzip-Response gecacht → zweiter Request aus Cache → korrekt dekomprimiert |
| 3 | `Interaction-003: Retry after redirect target returns 503` | Redirect + Retry | 302 → Ziel gibt 503 → Retry → Erfolg |
| 4 | `Interaction-004: Cookie survives retry cycle` | Cookie + Retry | Set-Cookie → 503 → Retry → Cookie noch im Jar |
| 5 | `Interaction-005: Vary Accept-Encoding creates separate cache entries` | Cache + Vary + Compression | Request mit gzip → cached, Request mit br → cached separat |
| 6 | `Interaction-006: Redirect chain with cookies accumulated` | Cookie + Redirect | 3-Hop Redirect, jeder Hop setzt Cookie → alle 3 Cookies am Ende da |
| 7 | `Interaction-007: Cache hit bypasses retry logic` | Cache + Retry | Cacheable 200 → cached → zweiter Request aus Cache (kein Server-Kontakt) |

**Client-Konfiguration:** `configure: b => b.WithCookies().WithCache(policy).WithRetry(policy).WithRedirect()`

**Acceptance Criteria:**
- [ ] `FeatureInteractionIntegrationTests.cs` created with 7 tests
- [ ] All tests use `[Collection("Http1Integration")]` and `new Version(1, 1)`
- [ ] DisplayNames follow `Interaction-001` pattern
- [ ] Tests configure multiple BidiStages via `ClientHelper.CreateClient(..., configure: ...)`
- [ ] All 7 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/FeatureInteractionIntegrationTests.cs` (NEW)

---

### TASK-023-003: Feature Interaction Tests — H10 + H2 + TLS
**Description:** Als Entwickler möchte ich die Feature-Interaktions-Tests für HTTP/1.0, HTTP/2 und TLS, damit Interaktionen über alle Versionen verifiziert sind.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-023-002 (reference pattern)
**Successors:** TASK-023-009
**Parallel:** no — needs reference from TASK-023-002

**Pattern:** Kopiere `FeatureInteractionIntegrationTests.cs`, ändere Version/Fixture/Collection/DisplayNames.

**Acceptance Criteria:**
- [ ] `FeatureInteractionH10IntegrationTests.cs` — 7 tests, `new Version(1, 0)`, `[Collection("Http1Integration")]`
- [ ] `FeatureInteractionH2IntegrationTests.cs` — 7 tests, `new Version(2, 0)`, `[Collection("Http2Integration")]`
- [ ] `FeatureInteractionTlsIntegrationTests.cs` — 7 tests, `new Version(1, 1)`, `scheme: "https"`, `[Collection("TlsIntegration")]`
- [ ] DisplayNames: `Interaction-H10-001`, `Interaction-H2-001`, `Interaction-TLS-001`
- [ ] All 21 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/FeatureInteractionH10IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/FeatureInteractionH2IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/FeatureInteractionTlsIntegrationTests.cs` (NEW)

---

### TASK-023-004: Resilience Tests — HTTP/1.1 (Reference Implementation)
**Description:** Als Entwickler möchte ich Tests für fehlerhafte Server-Antworten, damit sichergestellt ist dass der Client graceful reagiert (Exception, Timeout, oder Fehlermeldung — kein Hang/Crash).

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-023-001 (routes must exist)
**Successors:** TASK-023-005
**Parallel:** yes — can run alongside TASK-023-002, TASK-023-003, TASK-023-006, TASK-023-008

**Test-Szenarien (8 Tests):**

| # | DisplayName | Route | Erwartetes Verhalten |
|---|-------------|-------|---------------------|
| 1 | `Resilience-001: Content-Length mismatch causes exception or timeout` | `/resilience/content-length-mismatch` | Exception oder Timeout — kein Hang |
| 2 | `Resilience-002: Corrupt gzip data causes graceful failure` | `/resilience/corrupt-gzip` | Exception bei Body-Read — kein Crash |
| 3 | `Resilience-003: Corrupt brotli data causes graceful failure` | `/resilience/corrupt-br` | Exception bei Body-Read |
| 4 | `Resilience-004: Truncated body detected` | `/resilience/truncated-body/4` | Exception oder kurzer Body |
| 5 | `Resilience-005: Slow headers within timeout succeed` | `/resilience/slow-headers/500` | Response OK (Timeout 30s) |
| 6 | `Resilience-006: Slow body within timeout succeed` | `/resilience/slow-body/500` | Body vollständig empfangen |
| 7 | `Resilience-007: Slow headers exceed timeout cause cancellation` | `/resilience/slow-headers/10000` | OperationCanceledException (Timeout 3s) |
| 8 | `Resilience-008: Empty response causes exception` | `/resilience/empty-response` | Exception — kein Hang |

**Acceptance Criteria:**
- [ ] `ResilienceIntegrationTests.cs` created with 8 tests
- [ ] Tests use short timeouts (3-5s) for expected failures, 30s for expected success
- [ ] Each failure scenario verified with `Assert.ThrowsAnyAsync<Exception>` or `Assert.ThrowsAnyAsync<OperationCanceledException>`
- [ ] No test hangs indefinitely
- [ ] DisplayNames follow `Resilience-001` pattern
- [ ] All 8 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/ResilienceIntegrationTests.cs` (NEW)

---

### TASK-023-005: Resilience Tests — H10 + H2 + TLS
**Description:** Als Entwickler möchte ich Resilience-Tests für alle anderen HTTP-Versionen.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-023-004 (reference pattern), TASK-023-001 (routes)
**Successors:** TASK-023-009
**Parallel:** no — needs reference from TASK-023-004

**Hinweise:**
- HTTP/1.0: Alle 8 Szenarien anwendbar (gleiche Route-Infrastruktur)
- HTTP/2: Content-Length-Mismatch und truncated body verhalten sich anders (Streams, RST_STREAM). Ggf. Tests anpassen — Exception statt Timeout erwartet. `empty-response` ist im HTTP/2-Kontext ein GOAWAY/RST_STREAM.
- TLS: Alle Szenarien über HTTPS — identisches Verhalten erwartet

**Acceptance Criteria:**
- [ ] `ResilienceH10IntegrationTests.cs` — 8 tests adapted for HTTP/1.0
- [ ] `ResilienceH2IntegrationTests.cs` — 8 tests adapted for HTTP/2 (H2-spezifische Fehlermodi)
- [ ] `ResilienceTlsIntegrationTests.cs` — 8 tests over HTTPS
- [ ] DisplayNames: `Resilience-H10-001`, `Resilience-H2-001`, `Resilience-TLS-001`
- [ ] HTTP/2 tests may expect different exception types (e.g., `HttpRequestException` instead of `OperationCanceledException` for RST_STREAM)
- [ ] All 24 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/ResilienceH10IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/ResilienceH2IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/ResilienceTlsIntegrationTests.cs` (NEW)

---

### TASK-023-006: Request Compression Tests — HTTP/1.1 (Reference Implementation)
**Description:** Als Entwickler möchte ich end-to-end Tests für Request-Body-Kompression (ContentEncodingBidiStage mit RequestCompressionPolicy), damit verifiziert ist dass der Client Bodies korrekt komprimiert sendet.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-023-001 (routes + builder extensions)
**Successors:** TASK-023-007
**Parallel:** yes — can run alongside TASK-023-002, TASK-023-004, TASK-023-008

**Client-Konfiguration:** `configure: b => b.WithRequestCompression(new RequestCompressionPolicy { Encoding = "gzip" })`

**Test-Szenarien (6 Tests):**

| # | DisplayName | Encoding | Was wird geprüft |
|---|-------------|----------|-----------------|
| 1 | `ReqCompress-001: gzip request body sent and verified by server` | gzip | POST 4KB → Server verifiziert gzip → echot dekomprimiert |
| 2 | `ReqCompress-002: deflate request body sent and verified` | deflate | POST 4KB → Server verifiziert deflate |
| 3 | `ReqCompress-003: brotli request body sent and verified` | br | POST 4KB → Server verifiziert brotli |
| 4 | `ReqCompress-004: small body below threshold NOT compressed` | gzip | POST 100 bytes → Server sieht kein Content-Encoding |
| 5 | `ReqCompress-005: Content-Encoding header set correctly` | gzip | Verify `X-Content-Encoding` echo header == "gzip" |
| 6 | `ReqCompress-006: compressed request + decompressed response roundtrip` | gzip | POST gzip → Response gzip → beide Seiten korrekt |

**Acceptance Criteria:**
- [ ] `RequestCompressionIntegrationTests.cs` created with 6 tests
- [ ] Tests use `WithRequestCompression()` via `configure` callback
- [ ] Threshold test verifies 100-byte body is NOT compressed (< 1024 default)
- [ ] Roundtrip test sends compressed body AND receives compressed response
- [ ] DisplayNames follow `ReqCompress-001` pattern
- [ ] All 6 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/RequestCompressionIntegrationTests.cs` (NEW)

---

### TASK-023-007: Request Compression Tests — H10 + H2 + TLS
**Description:** Als Entwickler möchte ich Request-Compression-Tests für alle HTTP-Versionen.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-023-006 (reference pattern), TASK-023-001 (routes + extensions)
**Successors:** TASK-023-009
**Parallel:** no — needs reference from TASK-023-006

**Acceptance Criteria:**
- [ ] `RequestCompressionH10IntegrationTests.cs` — 6 tests, `new Version(1, 0)`
- [ ] `RequestCompressionH2IntegrationTests.cs` — 6 tests, `new Version(2, 0)`
- [ ] `RequestCompressionTlsIntegrationTests.cs` — 6 tests, `scheme: "https"`
- [ ] DisplayNames: `ReqCompress-H10-001`, `ReqCompress-H2-001`, `ReqCompress-TLS-001`
- [ ] All 18 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/RequestCompressionH10IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/RequestCompressionH2IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/RequestCompressionTlsIntegrationTests.cs` (NEW)

---

### TASK-023-008: Custom Handler Pipeline Tests
**Description:** Als Entwickler möchte ich Integration Tests für die Custom Handler Pipeline (`TurboHandler`, `UseRequest()`, `UseResponse()`, `AddHandler<T>()`), damit die Handler-Komposition end-to-end verifiziert ist.

**Token Estimate:** ~50k tokens
**Predecessors:** none
**Successors:** TASK-023-009
**Parallel:** yes — can run alongside TASK-023-001 through TASK-023-007

**Test-Szenarien — HTTP/1.1 (8 Tests):**

| # | DisplayName | Handler-Typ | Was wird geprüft |
|---|-------------|-------------|-----------------|
| 1 | `Handler-001: UseRequest injects custom header` | `UseRequest` | Header X-Custom-Injected wird gesetzt → `/headers/echo` bestätigt |
| 2 | `Handler-002: UseResponse adds header to response` | `UseResponse` | Response bekommt X-Handler-Added Header |
| 3 | `Handler-003: AddHandler typed handler processes request` | `AddHandler<T>` | Custom TurboHandler-Subklasse modifiziert Request |
| 4 | `Handler-004: Multiple handlers execute in registration order` | `UseRequest` ×2 | Handler 1 setzt X-First, Handler 2 setzt X-Second → beide vorhanden |
| 5 | `Handler-005: Handler sees original request on response` | `UseResponse` | `ProcessResponse(original, response)` — original hat Original-URL |
| 6 | `Handler-006: Handler works with redirect pipeline` | `UseRequest` + Redirect | Handler injiziert Header → Redirect → Header noch da nach Redirect? |
| 7 | `Handler-007: Handler works with compression pipeline` | `UseResponse` + Compression | Handler sieht dekomprimierte Response |
| 8 | `Handler-008: Handler works with cookie pipeline` | `UseRequest` + Cookie | Handler + Cookie-Injection — beide Header vorhanden |

**HTTP/2 Variante (4 Tests):** Tests 1, 3, 4, 6 über HTTP/2 um zu verifizieren dass Handler protocol-agnostic sind.

**Acceptance Criteria:**
- [ ] `HandlerPipelineIntegrationTests.cs` created (HTTP/1.1, 8 tests)
- [ ] `HandlerPipelineH2IntegrationTests.cs` created (HTTP/2, 4 ausgewählte Tests)
- [ ] Custom `TestHeaderHandler : TurboHandler` Klasse im Test-File definiert
- [ ] Tests nutzen `configure: b => b.UseRequest(...)`, `b.UseResponse(...)`, `b.AddHandler<T>()`
- [ ] DisplayNames follow `Handler-001` / `Handler-H2-001`
- [ ] All 12 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/HandlerPipelineIntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/HandlerPipelineH2IntegrationTests.cs` (NEW)

---

### TASK-023-009: Verification Gate
**Description:** Als Entwickler möchte ich verifizieren dass alle neuen Tests grün sind, der Build clean ist, und keine Regressionen existieren.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-023-003, TASK-023-005, TASK-023-007, TASK-023-008
**Successors:** none
**Parallel:** no — final gate

**Acceptance Criteria:**
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` — zero errors, zero warnings
- [ ] `dotnet test src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj` — all tests pass
- [ ] New test count: ~96 additional tests (7+21 interactions + 8+24 resilience + 6+18 compression + 12 handlers)
- [ ] 3 consecutive test runs pass (kein Flaky)
- [ ] `dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj` — existing unit tests still pass (no regressions from builder extensions)
- [ ] `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` — existing stream tests still pass

**Files:** (read-only verification)

---

## Task Dependency Graph

```
TASK-023-001 (Routes+Builder) ──→ TASK-023-004 (Resilience H11) ──→ TASK-023-005 (Resilience H10+H2+TLS) ──→ TASK-023-009
                               ├→ TASK-023-006 (ReqCompress H11) ──→ TASK-023-007 (ReqCompress H10+H2+TLS) ─┘
TASK-023-002 (Interaction H11) ──→ TASK-023-003 (Interaction H10+H2+TLS) ───────────────────────────────────→ TASK-023-009
TASK-023-008 (Handler H11+H2) ─────────────────────────────────────────────────────────────────────────────→ TASK-023-009
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-023-001 | ~40k | none | yes (with 002, 008) | — |
| TASK-023-002 | ~45k | none | yes (with 001, 008) | — |
| TASK-023-003 | ~40k | 002 | no | — |
| TASK-023-004 | ~50k | 001 | yes (with 002, 006, 008) | — |
| TASK-023-005 | ~40k | 001, 004 | no | — |
| TASK-023-006 | ~40k | 001 | yes (with 002, 004, 008) | — |
| TASK-023-007 | ~35k | 001, 006 | no | — |
| TASK-023-008 | ~50k | none | yes (with 001, 002) | — |
| TASK-023-009 | ~15k | 003, 005, 007, 008 | no | — |

**Total estimated tokens:** ~355k

## Functional Requirements

- FR-1: `WithRequestCompression(policy?)` Extension Method muss `RequestCompressionPolicy` im `TurboClientDescriptor` setzen
- FR-2: `WithExpectContinue(policy?)` Extension Method muss `Expect100Policy` im `TurboClientDescriptor` setzen
- FR-3: Feature-Interaction-Tests müssen mindestens 2 BidiStages gleichzeitig über `configure` aktivieren
- FR-4: Resilience-Routen müssen kaputte Antworten senden (Content-Length Mismatch, korrupte Compression, truncated Body)
- FR-5: Resilience-Tests dürfen NICHT hängen — jeder Test hat ein Timeout (max 30s für Erfolg, max 5s für erwartete Fehler)
- FR-6: Request-Compression-Tests müssen verifizieren dass der Server die komprimierten Bytes empfangen hat (via Echo-Header)
- FR-7: Handler-Tests müssen `UseRequest()`, `UseResponse()` und `AddHandler<T>()` abdecken
- FR-8: Handler-Ordering muss FIFO sein (Registrierungsreihenfolge = Ausführungsreihenfolge)
- FR-9: Alle Tests folgen dem `DisplayName`-Muster: `Category-VERSION-NNN: description`
- FR-10: Alle Tests nutzen `CancellationTokenSource` mit explizitem Timeout

## Non-Goals

- HTTP/3 Tests (QUIC nicht stabil)
- Änderungen an Production BidiStages (nur Tests + Builder Extensions)
- Performance/Throughput Messungen (Feature 021 hat Concurrency-Tests)
- TracingBidiStage Tests (Activity-Propagation ist ein separates Thema)
- Connection Pool Lifecycle Tests (idle eviction, pool exhaustion — separates Feature)
- HEAD-Methode Fix (bekannter Bug in Http11DecoderStage — separater Bugfix)

## Technical Considerations

- **Builder Extensions:** `WithRequestCompression()` und `WithExpectContinue()` sind minimale API-Ergänzungen die dem bestehenden Pattern folgen (`WithCookies()`, `WithCache()`, etc.). Kein Breaking Change.
- **Resilience Route Implementation:** Die `/resilience/content-length-mismatch` Route muss den Body manuell schreiben (nicht via `Results.Content`) um Content-Length Mismatch zu erzeugen. Pattern: `ctx.Response.ContentLength = 1000; await ctx.Response.Body.WriteAsync(new byte[500]); await ctx.Response.Body.FlushAsync();`
- **Request Compression Verification:** Der Server empfängt komprimierte Bytes im Body. Die `/compress/verify-gzip` Route muss `GZipStream` zum Dekomprimieren nutzen und den dekomprimierten Body zurücksenden.
- **HTTP/2 Resilience:** Bei HTTP/2 werden manche Fehler als RST_STREAM statt Connection-Close signalisiert. Die H2-Resilience-Tests müssen ggf. andere Exception-Typen erwarten.
- **ClientHelper configure:** Der bestehende `configure` Parameter in `ClientHelper.CreateClient()` ermöglicht Builder-Konfiguration ohne Änderung am ClientHelper selbst.
- **Test Isolation:** Feature-Interaction-Tests die Cache nutzen brauchen pro Test eine unique URL (z.B. `/cache/max-age/60?t={testId}`) damit sich Tests nicht gegenseitig beeinflussen.

## Success Metrics

- ~96 neue Integration Tests über 14 neue Dateien
- Alle BidiStage-Kombinationen mindestens 1x end-to-end verifiziert
- Zero Resilience-Tests die hängen (alle mit Timeout)
- Request-Compression end-to-end für gzip, deflate und brotli verifiziert
- Handler-Pipeline mit 3 API-Varianten getestet

## Open Questions

_None — all questions resolved._
