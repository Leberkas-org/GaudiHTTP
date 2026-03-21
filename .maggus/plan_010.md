# Plan: TurboHttp Namespace Reorganisation

## Introduction

Das TurboHttp-Projekt ist auf 98 Produktionsdateien in 18 Namespaces gewachsen.
Einige Namespace-Namen sind irreführend (`Middleware`, `Lifecycle`), andere zu dünn
(`IO.Stages` mit 1 Datei, `Hosting` mit 1 Datei), und `Streams.Stages` ist mit
22 Dateien völlig flach. Ziel ist eine saubere, konsistente Namespace-Hierarchie,
die dem tatsächlichen Zweck der Komponenten entspricht — ohne die öffentliche
`TurboHttp.Client.*`-API zu verändern.

**Entscheidungen:**
- `TurboHttp.Client.*` bleibt unberührt (breaking change ausgeschlossen)
- `TurboHttp.IO` → `TurboHttp.Transport`, `TurboHttp.Lifecycle` → `TurboHttp.Pooling`
- `TurboHttp.Middleware` + `TurboHttp.Hosting` → Root-Namespace `TurboHttp` (BCL-Stil)
- `TurboHttp.Streams.Stages` wird nach Funktion aufgeteilt: `Encoding`, `Decoding`, `Routing`, `Features`
- `TurboHttp.Internal.Stages` wird in `TurboHttp.Streams.Stages.Routing` aufgelöst

---

## Zielstruktur (Vorher → Nachher)

```
VORHER                              NACHHER
──────────────────────────────────────────────────────────────────
TurboHttp.Client.*                  TurboHttp.Client.*          (unverändert)
TurboHttp.Hosting                   TurboHttp                   (root namespace)
TurboHttp.Middleware                TurboHttp                   (root namespace)
TurboHttp.IO                        TurboHttp.Transport
TurboHttp.IO.Stages                 TurboHttp.Transport         (absorbiert)
TurboHttp.Lifecycle                 TurboHttp.Pooling
TurboHttp.Internal                  TurboHttp.Internal          (unverändert)
TurboHttp.Internal.Stages           TurboHttp.Streams.Stages.Routing (absorbiert)
TurboHttp.Protocol.*                TurboHttp.Protocol.*        (unverändert)
TurboHttp.Streams                   TurboHttp.Streams           (unverändert)
TurboHttp.Streams.Stages            aufgeteilt (s.u.)
  ├── Encoder-Stages                TurboHttp.Streams.Stages.Encoding
  ├── Decoder-Stages                TurboHttp.Streams.Stages.Decoding
  ├── Routing-Stages                TurboHttp.Streams.Stages.Routing
  └── Feature-Stages                TurboHttp.Streams.Stages.Features
```

---

## Zuweisung: Streams.Stages Dateien

| Datei | Neuer Sub-Namespace |
|-------|---------------------|
| Http10EncoderStage.cs | Encoding |
| Http11EncoderStage.cs | Encoding |
| Http20EncoderStage.cs | Encoding |
| PrependPrefaceStage.cs | Encoding |
| Request2FrameStage.cs | Encoding |
| Http10DecoderStage.cs | Decoding |
| Http11DecoderStage.cs | Decoding |
| Http20DecoderStage.cs | Decoding |
| Http20StreamStage.cs | Decoding |
| ExtractOptionsStage.cs | Routing |
| Http1XCorrelationStage.cs | Routing |
| Http20CorrelationStage.cs | Routing |
| StreamIdAllocatorStage.cs | Routing |
| RequestEnricherStage.cs | Routing |
| GroupByHostKeyStage.cs | Routing (von Internal.Stages) |
| HostKeyGroupByExtensions.cs | Routing (von Internal.Stages) |
| HostKeyMergeBack.cs | Routing (von Internal.Stages) |
| MergeSubstreamsStage.cs | Routing (von Internal.Stages) |
| CacheLookupStage.cs | Features |
| CacheStorageStage.cs | Features |
| ConnectionReuseStage.cs | Features |
| CookieInjectionStage.cs | Features |
| CookieStorageStage.cs | Features |
| DecompressionStage.cs | Features |
| Http20ConnectionStage.cs | Features |
| MiddlewareRequestStage.cs | Features |
| MiddlewareResponseStage.cs | Features |
| RedirectStage.cs | Features |
| RetryStage.cs | Features |
| TurboAttributes.cs | Streams.Stages (Root, bleibt) |

---

## Goals

- Alle irreführenden Namespace-Namen beseitigen (`Middleware`, `Lifecycle`, `IO.Stages`)
- `Streams.Stages` in 4 kohärente Funktions-Namespaces aufteilen (Encoding/Decoding/Routing/Features)
- `TurboHttp.Client.*` bleibt vollständig unverändert — keine Breaking Changes für Nutzer der Public API
- Alle Test-Projekte aktualisieren (`TurboHttp.Tests`, `TurboHttp.StreamTests`, `TurboHttp.IntegrationTests`)
- Build nach jeder Aufgabe: 0 Errors, alle Tests grün

---

## User Stories

### TASK-001: IO → Transport (Ordner umbenennen + IO.Stages absorbieren)

**Description:** Als Entwickler möchte ich, dass das `TurboHttp.IO`-Namespace in
`TurboHttp.Transport` umbenannt wird, damit der Name den Zweck (TCP-Transport,
Byte-Moving) klar kommuniziert. `IO.Stages/ConnectionStage.cs` wird in `Transport`
absorbiert (kein separater Sub-Namespace für 1 Datei).

**Betroffene Dateien (Production):**
```
IO/ClientByteMover.cs          → namespace TurboHttp.Transport
IO/ClientManager.cs            → namespace TurboHttp.Transport
IO/ClientRunner.cs             → namespace TurboHttp.Transport
IO/ClientState.cs              → namespace TurboHttp.Transport
IO/IClientProvider.cs          → namespace TurboHttp.Transport
IO/TcpOptionsFactory.cs        → namespace TurboHttp.Transport
IO/Stages/ConnectionStage.cs   → namespace TurboHttp.Transport (Datei in Transport/ verschieben)
```

**Acceptance Criteria:**
- [ ] Ordner `TurboHttp/IO/Stages/` wird geleert und gelöscht
- [ ] Alle 7 Dateien deklarieren `namespace TurboHttp.Transport`
- [ ] Alle `using TurboHttp.IO;` und `using TurboHttp.IO.Stages;` im gesamten Solution ersetzt
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 Errors
- [ ] `dotnet test src/TurboHttp.sln` → alle Tests bestehen

---

### TASK-002: Lifecycle → Pooling (Ordner umbenennen)

**Description:** Als Entwickler möchte ich, dass das `TurboHttp.Lifecycle`-Namespace
in `TurboHttp.Pooling` umbenannt wird, damit klar ist, dass es sich um
Connection-Pool-Management per Actor-Hierarchie handelt.

**Betroffene Dateien (Production):**
```
Lifecycle/ConnectionActor.cs   → namespace TurboHttp.Pooling
Lifecycle/ConnectionHandle.cs  → namespace TurboHttp.Pooling
Lifecycle/ConnectionState.cs   → namespace TurboHttp.Pooling
Lifecycle/HostPool.cs          → namespace TurboHttp.Pooling
Lifecycle/PoolRouter.cs        → namespace TurboHttp.Pooling
```

**Acceptance Criteria:**
- [ ] Ordner `TurboHttp/Lifecycle/` umbenannt zu `TurboHttp/Pooling/`
- [ ] Alle 5 Dateien deklarieren `namespace TurboHttp.Pooling`
- [ ] Alle `using TurboHttp.Lifecycle;` im gesamten Solution ersetzt
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 Errors
- [ ] `dotnet test src/TurboHttp.sln` → alle Tests bestehen

---

### TASK-003: Middleware + Hosting → TurboHttp (Root Namespace)

**Description:** Als Nutzer der Bibliothek möchte ich Builder und DI-Extensions
direkt unter `TurboHttp` nutzen (wie `HttpClient` in `System.Net.Http`), ohne
tiefe Sub-Namespace-Imports.

**Betroffene Dateien (Production):**
```
Middleware/ITurboHttpClientBuilder.cs       → namespace TurboHttp
Middleware/TurboClientDescriptor.cs         → namespace TurboHttp
Middleware/TurboHttpClientBuilder.cs        → namespace TurboHttp
Middleware/TurboHttpClientBuilderExtensions.cs → namespace TurboHttp
Middleware/TurboMiddleware.cs               → namespace TurboHttp
Hosting/TurboClientServiceCollectionExtensions.cs → namespace TurboHttp
```

**Hinweis zu Ordnerstruktur:** Dateien bleiben physisch in ihren Unterordnern
(`Middleware/`, `Hosting/`) zur Übersichtlichkeit — nur die `namespace`-Deklaration
ändert sich auf `TurboHttp`.

**Acceptance Criteria:**
- [ ] Alle 6 Dateien deklarieren `namespace TurboHttp`
- [ ] Alle `using TurboHttp.Middleware;` und `using TurboHttp.Hosting;` im gesamten Solution ersetzt (inkl. Test-Projekte)
- [ ] `TurboHttp.Client.*`-Namespaces bleiben vollständig unverändert
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 Errors
- [ ] `dotnet test src/TurboHttp.sln` → alle Tests bestehen

---

### TASK-004: Streams.Stages.Encoding — Encoder-Stages auslagern

**Description:** Als Entwickler möchte ich alle Encoding-Stages (Request→Bytes)
in einem eigenen Sub-Namespace `TurboHttp.Streams.Stages.Encoding` bündeln.

**Betroffene Dateien:**
```
Streams/Stages/Http10EncoderStage.cs   → Streams/Stages/Encoding/
Streams/Stages/Http11EncoderStage.cs   → Streams/Stages/Encoding/
Streams/Stages/Http20EncoderStage.cs   → Streams/Stages/Encoding/
Streams/Stages/PrependPrefaceStage.cs  → Streams/Stages/Encoding/
Streams/Stages/Request2FrameStage.cs   → Streams/Stages/Encoding/
```
Neuer Namespace: `TurboHttp.Streams.Stages.Encoding`

**Acceptance Criteria:**
- [ ] Ordner `TurboHttp/Streams/Stages/Encoding/` erstellt mit den 5 Dateien
- [ ] Alle 5 Dateien deklarieren `namespace TurboHttp.Streams.Stages.Encoding`
- [ ] Alle Engines (`Http10Engine`, `Http11Engine`, `Http20Engine`) aktualisiert
- [ ] Alle referenzierenden Test-Dateien aktualisiert
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 Errors
- [ ] `dotnet test src/TurboHttp.sln` → alle Tests bestehen

---

### TASK-005: Streams.Stages.Decoding — Decoder-Stages auslagern

**Description:** Als Entwickler möchte ich alle Decoding-Stages (Bytes→Response)
in `TurboHttp.Streams.Stages.Decoding` bündeln.

**Betroffene Dateien:**
```
Streams/Stages/Http10DecoderStage.cs   → Streams/Stages/Decoding/
Streams/Stages/Http11DecoderStage.cs   → Streams/Stages/Decoding/
Streams/Stages/Http20DecoderStage.cs   → Streams/Stages/Decoding/
Streams/Stages/Http20StreamStage.cs    → Streams/Stages/Decoding/
```
Neuer Namespace: `TurboHttp.Streams.Stages.Decoding`

**Acceptance Criteria:**
- [ ] Ordner `TurboHttp/Streams/Stages/Decoding/` erstellt mit den 4 Dateien
- [ ] Alle 4 Dateien deklarieren `namespace TurboHttp.Streams.Stages.Decoding`
- [ ] Alle Engines und referenzierenden Streams-Dateien aktualisiert
- [ ] Alle referenzierenden Test-Dateien aktualisiert
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 Errors
- [ ] `dotnet test src/TurboHttp.sln` → alle Tests bestehen

---

### TASK-006: Streams.Stages.Routing — Routing-Stages + Internal.Stages absorbieren

**Description:** Als Entwickler möchte ich alle Flow-Control- und Correlation-Stages
(inkl. bisher in `Internal.Stages` versteckter Host-Routing-Stages) in
`TurboHttp.Streams.Stages.Routing` vereinen.

**Betroffene Dateien:**
```
Aus Streams/Stages/:
  ExtractOptionsStage.cs          → Streams/Stages/Routing/
  Http1XCorrelationStage.cs       → Streams/Stages/Routing/
  Http20CorrelationStage.cs       → Streams/Stages/Routing/
  StreamIdAllocatorStage.cs       → Streams/Stages/Routing/
  RequestEnricherStage.cs         → Streams/Stages/Routing/

Aus Internal/Stages/ (Namespace-Migration!):
  GroupByHostKeyStage.cs          → Streams/Stages/Routing/
  HostKeyGroupByExtensions.cs     → Streams/Stages/Routing/
  HostKeyMergeBack.cs             → Streams/Stages/Routing/
  MergeSubstreamsStage.cs         → Streams/Stages/Routing/
```
Neuer Namespace: `TurboHttp.Streams.Stages.Routing`

**Acceptance Criteria:**
- [ ] Ordner `TurboHttp/Streams/Stages/Routing/` erstellt mit allen 9 Dateien
- [ ] Alle 9 Dateien deklarieren `namespace TurboHttp.Streams.Stages.Routing`
- [ ] `TurboHttp/Internal/Stages/` Ordner leer und gelöscht
- [ ] Alle `using TurboHttp.Internal.Stages;` im gesamten Solution ersetzt
- [ ] Alle Engines, `Engine.cs`, und referenzierende Dateien aktualisiert
- [ ] Alle referenzierenden Test-Dateien aktualisiert
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 Errors
- [ ] `dotnet test src/TurboHttp.sln` → alle Tests bestehen

---

### TASK-007: Streams.Stages.Features — Feature-Stages auslagern

**Description:** Als Entwickler möchte ich alle höherwertigen HTTP-Semantik-Stages
(Cache, Cookies, Decompression, Redirect, Retry, Middleware-Pipeline, HTTP/2 Connection)
in `TurboHttp.Streams.Stages.Features` bündeln.

**Betroffene Dateien:**
```
Streams/Stages/CacheLookupStage.cs        → Streams/Stages/Features/
Streams/Stages/CacheStorageStage.cs       → Streams/Stages/Features/
Streams/Stages/ConnectionReuseStage.cs    → Streams/Stages/Features/
Streams/Stages/CookieInjectionStage.cs    → Streams/Stages/Features/
Streams/Stages/CookieStorageStage.cs      → Streams/Stages/Features/
Streams/Stages/DecompressionStage.cs      → Streams/Stages/Features/
Streams/Stages/Http20ConnectionStage.cs   → Streams/Stages/Features/
Streams/Stages/MiddlewareRequestStage.cs  → Streams/Stages/Features/
Streams/Stages/MiddlewareResponseStage.cs → Streams/Stages/Features/
Streams/Stages/RedirectStage.cs           → Streams/Stages/Features/
Streams/Stages/RetryStage.cs              → Streams/Stages/Features/
```
Neuer Namespace: `TurboHttp.Streams.Stages.Features`

**Acceptance Criteria:**
- [ ] Ordner `TurboHttp/Streams/Stages/Features/` erstellt mit allen 11 Dateien
- [ ] Alle 11 Dateien deklarieren `namespace TurboHttp.Streams.Stages.Features`
- [ ] Alle Engines und referenzierende Streams-Dateien aktualisiert
- [ ] Alle referenzierenden Test-Dateien aktualisiert
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 Errors
- [ ] `dotnet test src/TurboHttp.sln` → alle Tests bestehen

---

### TASK-008: Cleanup & Final Validation

**Description:** Als Entwickler möchte ich sicherstellen, dass nach allen Umbenennungen
keine alten Namespace-Referenzen mehr existieren, leere Ordner gelöscht sind
und Build + alle Tests sauber durchlaufen.

**Acceptance Criteria:**
- [ ] Keine `using TurboHttp.IO;`, `using TurboHttp.IO.Stages;` mehr im Solution
- [ ] Keine `using TurboHttp.Lifecycle;` mehr im Solution
- [ ] Keine `using TurboHttp.Middleware;` und `using TurboHttp.Hosting;` mehr im Solution
- [ ] Keine `using TurboHttp.Internal.Stages;` mehr im Solution
- [ ] Keine `using TurboHttp.Streams.Stages;` (unqualifiziert) für Dateien, die jetzt in Sub-Namespaces sind
- [ ] Leere Ordner `IO/Stages/`, `Internal/Stages/` gelöscht
- [ ] `Streams/Stages/` Root enthält nur noch `TurboAttributes.cs`
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 Errors, 0 Warnings (Namespace-bezogen)
- [ ] `dotnet test src/TurboHttp.sln` → alle Tests bestehen
- [ ] Grep über Solution nach alten Namespace-Strings → kein Treffer

---

## Functional Requirements

- FR-1: `TurboHttp.Client.*` (7 Dateien, öffentliche API) darf nicht verändert werden — kein einziger using, kein Namespace
- FR-2: Jede Aufgabe (TASK-001 bis TASK-008) muss unabhängig build- und testbar sein
- FR-3: Namespace-Deklaration muss exakt dem Ordnerpfad entsprechen (Konvention)
- FR-4: Keine Compatibility Shims, keine `[Obsolete]`-Weiterleitungen — sauberer Cut
- FR-5: Alle drei Test-Projekte (`TurboHttp.Tests`, `TurboHttp.StreamTests`, `TurboHttp.IntegrationTests`) müssen nach jeder Aufgabe grün sein
- FR-6: `TurboAttributes.cs` bleibt in `TurboHttp.Streams.Stages` (Root), nicht in einem Sub-Namespace

---

## Non-Goals

- Keine Umbenennung von Klassen oder Interfaces (nur Namespaces)
- Keine Änderungen an `TurboHttp.Protocol.*` (RFC-Struktur bleibt unverändert)
- Keine Änderungen an `TurboHttp.Streams` (Engine-Ebene, nicht Stages)
- Kein Zusammenführen von Protokoll-Stages mit Protocol-Layer
- Keine neuen Features oder Bugfixes im Rahmen dieser Aufgabe

---

## Technical Considerations

- **Reihenfolge ist wichtig**: TASK-001 und TASK-002 zuerst (isoliert, geringes Risiko), dann TASK-003, dann TASK-004 bis TASK-007 (können parallel bearbeitet werden, aber je eines bauen+testen), zuletzt TASK-008
- **Test-Projekte**: `TurboHttp.StreamTests` hat die meisten Stage-Referenzen — dort ist der größte using-Update-Aufwand
- **Engine-Dateien**: `Http10Engine.cs`, `Http11Engine.cs`, `Http20Engine.cs`, `Engine.cs` importieren viele Stages und müssen bei TASK-004 bis TASK-007 aktualisiert werden
- **`HostKeyGroupByExtensions.cs`**: Extension-Methoden-Datei — prüfen ob sie internal ist; wenn ja, kein Breaking Change durch Namespace-Wechsel
- **Keine globalen using-Direktiven**: Projekt verwendet kein `GlobalUsings.cs` für diese Namespaces — einzelne Dateien manuell aktualisieren

---

## Success Metrics

- 18 Namespaces → 14 Namespaces (4 eliminiert: `IO`, `IO.Stages`, `Lifecycle`, `Internal.Stages`)
- `Streams.Stages` von 22 flachen Dateien auf max. 1 (`TurboAttributes.cs`) reduziert
- `Middleware` und `Hosting` verschwinden als sichtbare Namespaces für Bibliotheksnutzer
- Grep nach `TurboHttp.IO`, `TurboHttp.Lifecycle`, `TurboHttp.Middleware`, `TurboHttp.Hosting`, `TurboHttp.Internal.Stages` → 0 Treffer nach TASK-008

---

## Open Questions

- Soll `TurboHttp.Internal` langfristig auch aufgelöst werden? `Messages.cs` und `RequestEndpoint.cs` könnten in Root oder `Pooling` — ist nicht Teil dieses Plans, aber ein logischer nächster Schritt.
- Soll `Middleware/` Ordner nach TASK-003 in `Builder/` umbenannt werden, um die physische Struktur klarer zu machen? (Low-Priority, optionale Schönheitskorrektur)
