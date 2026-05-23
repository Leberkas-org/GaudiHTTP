# Server Test Coverage Map

**Goal:** Priorisierte Liste aller fehlenden Server-Tests mit Zuordnung zu Test-Projekten (Unit, Server-Integration, E2E).

**Methodik:** Risiko-basierte Priorisierung — Features die bei Bugs den meisten Schaden anrichten zuerst.

**Status:** Referenz-Dokument für zukünftige Test-Implementierungen. Kein Implementierungsplan — Tests werden in separaten Batches geschrieben.

---

## Legende

- **Unit (Tests/)** — Isolierte Tests mit Mocks/Fakes in `TurboHTTP.Tests/`
- **Server (.Server/)** — TurboHTTP Server auf localhost:0, Standard-HttpClient als Consumer in `TurboHTTP.IntegrationTests.Server/`
- **E2E (.E2E/)** — TurboHttpClient gegen TurboHTTP Server über Loopback in `TurboHTTP.IntegrationTests.E2E/`

Zellen:
- `✓` = Test existiert bereits
- Beschreibung = Test fehlt, das ist was getestet werden soll
- `-` = Nicht sinnvoll für diesen Test-Typ

---

## Bestandsaufnahme: Was ist bereits abgedeckt

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| Route Registration (MapTurboGet/Post/Put/Delete/Patch) | ✓ | ✓ | - |
| Route Groups (MapTurboGroup) | ✓ | - | - |
| Static Path Matching | ✓ | ✓ | - |
| Parameter Extraction (Route Values) | ✓ | - | - |
| 404 Not Found | ✓ | ✓ | - |
| SSE (EventStream) | ✓ | ✓ | ✓ |
| Content-Type Handling | ✓ | ✓ | - |
| Status Code Setting | ✓ | ✓ | - |
| Streaming via Akka Sources | ✓ | ✓ | ✓ |
| TLS/HTTPS Endpoints | - | ✓ | - |
| Client Certificate (Allow/Require) | - | ✓ | - |
| SNI Certificate Selection | - | ✓ | - |
| TLS Handshake Features | - | ✓ | - |
| AddTurboKestrel DI Registration | ✓ | ✓ | - |
| Remote IP Exposure | ✓ | ✓ | - |
| Connection Info (basic) | ✓ | ✓ | - |
| Middleware Pipeline (Use/Run/Map/MapWhen) | ✓ | - | - |
| Parameter Binding (all sources) | ✓ | - | - |
| Complex Type Binding ([AsParameters]) | ✓ | - | - |
| Entity Routing (Ask/Tell) | ✓ | - | - |
| DelegateHandlerBinder | ✓ | - | - |
| Error Handling (500 on exception) | ✓ | - | - |

---

## Priorität 1: Kritisch

Features die bei Bugs Produktionsausfälle, Ressourcenlecks oder Datenverlust verursachen können.

### 1.1 Timeout-Enforcement

Kein einziger Test prüft ob konfigurierte Timeouts tatsächlich Connections schließen.

| Timeout | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| KeepAlive — Connection schließt nach Inaktivität | - | Idle Connection, warten, prüfen ob geschlossen | - |
| RequestHeaders — Timeout bei unvollständigen Headers | - | Partial Request senden, Timeout abwarten | - |
| BodyConsumption — Timeout bei langsamem Request-Body | - | Body langsam senden, prüfen ob abgebrochen | - |

### 1.2 Connection Limits

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| MaxConcurrentConnections — Limit wird enforced | - | N+1 Connections öffnen, letzte wird abgelehnt | - |

### 1.3 Graceful Shutdown

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| Laufende Requests werden abgewartet | - | Request starten, Server stoppen, Response prüfen | - |
| Neue Requests werden abgelehnt | - | Server stoppen, neuen Request versuchen | - |
| Shutdown-Timeout erzwingt Abbruch | - | Langsamen Handler starten, Shutdown-Timeout kurz setzen | - |

### 1.4 Response Body Streaming (HTTP/1.1)

Der Body-Encoder für HTTP/1.1 wurde gerade erst verdrahtet. Kein Integration-Test bestätigt dass Chunked Transfer-Encoding korrekt funktioniert.

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| Chunked Response ohne Content-Length | - | Response ohne CL, Transfer-Encoding: chunked prüfen | Chunked Body via TurboHttpClient empfangen |
| Content-Length Response | - | Response mit CL, Exact-Length prüfen | - |
| Leere Response (204/304) | - | Status setzen, leerer Body bestätigen | - |

### 1.5 Response Headers

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| Custom Response Headers | - | Header setzen, am HttpClient prüfen | - |
| Multiple Values für selben Header | - | Mehrere Werte, alle empfangen | - |

---

## Priorität 2: Hoch

Feature-Korrektheit — Tests die bestätigen dass Features über echtes HTTP korrekt funktionieren (nicht nur mit Mocks).

### 2.1 Parameter Binding über HTTP

Unit-Tests existieren für alle Binding-Sources, aber kein Test geht durch echtes HTTP.

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| Route Parameter (`/users/{id}`) | ✓ | GET /users/42, Handler empfängt id=42 | - |
| Query String (`?name=value`) | ✓ | GET /search?q=test, Handler empfängt q="test" | - |
| Header Binding (`[FromHeader]`) | ✓ | X-Custom: value, Handler empfängt Wert | - |
| Multiple Query Params | ✓ | ?a=1&b=2, beide gebunden | - |
| Optional Parameter (nullable) | ✓ | Ohne Query Param, null im Handler | - |

### 2.2 Request Body über HTTP

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| JSON POST Body | - | POST mit JSON, deserialisiert im Handler | POST Echo via TurboHttpClient |
| Form-Encoded Body | ✓ | POST mit x-www-form-urlencoded | - |
| Large Body (> BufferThreshold) | - | Body > 64KB, Streaming-Empfang prüfen | - |

### 2.3 Middleware über HTTP

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| Middleware setzt Response Header | ✓ | Middleware fügt X-Powered-By hinzu, Client sieht es | - |
| Middleware modifiziert Request | ✓ | Middleware setzt Header, Handler sieht ihn | - |
| Map (Path-Branching) | ✓ | /api/* → Middleware A, /admin/* → Middleware B | - |
| Exception in Handler → 500 | ✓ | Handler wirft, 500 Response | - |
| Exception in Middleware → 500 | ✓ | Middleware wirft, 500 Response | - |

### 2.4 Entity Routing über HTTP

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| Ask-Pattern mit Echo-Actor | ✓ | GET /entity/123, Actor antwortet, Client empfängt | - |
| Tell-Pattern (fire-and-forget) | ✓ | POST /entity/123, 202 Accepted | - |
| Multiple Methods pro Entity | ✓ | GET + POST auf selber Entity | - |

### 2.5 Raw Byte Streaming

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| TurboStreamResults.Stream() | ✓ | Binary Source → Client empfängt Bytes | - |
| Custom Content-Type bei Stream | ✓ | application/octet-stream gesetzt | - |

---

## Priorität 3: Mittel

Konfiguration und Edge Cases die selten aber schmerzhaft brechen.

### 3.1 Protocol-spezifische Options

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| Http1ServerOptions Defaults | Defaults + Ranges prüfen | - | - |
| Http2ServerOptions Defaults | Defaults + Ranges prüfen | - | - |
| Http3ServerOptions Defaults | Defaults + Ranges prüfen | - | - |

### 3.2 Body Buffering/Chunking Konfiguration

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| BodyBufferThreshold Enforcement | - | Request < Threshold: buffered. > Threshold: streamed | - |
| ResponseBodyChunkSize | - | Große Response, Chunk-Größe im Wire prüfen | - |

### 3.3 Connection Info Details

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| LocalIP + LocalPort | - | Handler liest Connection-Info, alle Felder befüllt | - |
| Protocol Version | - | HTTP/1.1 vs HTTP/2, Version korrekt im Context | - |

### 3.4 HTTP/2 Spezifisch

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| h2c Upgrade über HTTP | ✓ | HTTP/1.1 → H2 Upgrade in Integration | - |
| Multiple Concurrent Streams | - | - | Mehrere parallele Requests auf einer Connection |

---

## Priorität 4: Niedrig

Nice-to-Have — geringstes Risiko, kann als letztes angegangen werden.

### 4.1 QUIC/HTTP3

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| QUIC Listener Binding | - | - | HTTP/3 Smoke Test (wenn QUIC verfügbar) |

### 4.2 TLS Konfiguration

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| ConfigureHttpsDefaults | Defaults werden angewendet | - | - |

### 4.3 Routing Edge Cases

| Feature | Unit | Server | E2E |
|---------|:---:|:---:|:---:|
| MapTurboMethods (Multi-Method) | ✓ | GET + POST auf selber Route | - |
| Form File Upload (Multipart) | ✓ | Echter File-Upload über HTTP | - |

---

## Zusammenfassung

| Priorität | Fehlende Tests | Geschätzter Aufwand |
|-----------|---------------|---------------------|
| **P1 Kritisch** | 13 Tests | Groß — Timeout/Shutdown-Tests sind komplex |
| **P2 Hoch** | 18 Tests | Mittel — Integration-Tests für existierende Unit-Tests |
| **P3 Mittel** | 8 Tests | Klein — Konfiguration + Edge Cases |
| **P4 Niedrig** | 4 Tests | Klein — Nice-to-Have |
| **Gesamt** | ~43 Tests | |

Empfohlene Batch-Reihenfolge:
1. P2 zuerst (Quick Wins — Unit-Tests existieren bereits, nur Integration fehlt)
2. P1.4 + P1.5 (Response Body + Headers — gerade implementiert, sollte sofort getestet werden)
3. P1.1–P1.3 (Timeouts, Limits, Shutdown — komplex, eigener Batch)
4. P3 + P4 (bei Gelegenheit)
