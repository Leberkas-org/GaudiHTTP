# Cookies, Alt-Svc, and Diagnostics

## Cookies

### Requirement: Pluggable cookie storage via ICookieStore

The cookie subsystem delegates persistence to an `ICookieStore` implementation. The store is accessed on a single logical thread per request pipeline and is not required to be thread-safe. The default `MemoryCookieStore` keeps entries in-process only.

#### Scenario: Store exposes CRUD by (name, domain, path) triple
- **WHEN** `Add` is called with a `CookieStoreEntry`
- **THEN** the entry is retrievable via `GetAll` and `Count` increments by one

#### Scenario: Remove targets exact (name, domain, path) match
- **WHEN** `Remove` is called with a given name, domain, and path
- **THEN** only the entry matching all three (name case-insensitive, domain case-insensitive, path case-sensitive) is deleted

#### Scenario: Clear empties the store
- **WHEN** `Clear` is called
- **THEN** `Count` returns zero and `GetAll` returns an empty list

### Requirement: Set-Cookie response processing (RFC 6265 section 5.2-5.3)

`CookieJar.ProcessResponse` parses each `Set-Cookie` header from the response, replaces any existing entry with the same (name, domain, path) triple, and stores non-expired results.

#### Scenario: New cookie is stored from Set-Cookie header
- **WHEN** a response contains a `Set-Cookie` header with a valid name=value pair
- **THEN** the cookie is parsed and added to the store

#### Scenario: Existing cookie is replaced on re-set
- **WHEN** a response sets a cookie with the same name, domain, and path as an existing entry
- **THEN** the old entry is removed before the new one is added

#### Scenario: Expired cookie on receipt is not stored
- **WHEN** a `Set-Cookie` header specifies an `Expires` date in the past or `Max-Age=0`
- **THEN** the cookie is removed from the store (if present) and not re-added

#### Scenario: Max-Age takes precedence over Expires
- **WHEN** both `Max-Age` and `Expires` attributes are present
- **THEN** the expiry is computed from `Max-Age` relative to the current time, ignoring `Expires`

### Requirement: Domain and path scoping (RFC 6265 section 5.1.3, 5.1.4)

Cookies are scoped by domain and path. A cookie without a `Domain` attribute is host-only. Domain matching supports subdomain coverage; path matching requires prefix with separator semantics.

#### Scenario: Host-only cookie matches exact host only
- **WHEN** a cookie was set without a `Domain` attribute
- **THEN** it is sent only to requests whose host exactly matches the request host (case-insensitive)

#### Scenario: Domain cookie matches subdomains
- **WHEN** a cookie has a `Domain` attribute (e.g., `example.com`)
- **THEN** it is sent to `example.com` and `sub.example.com` but not to `otherexample.com`

#### Scenario: Domain attribute rejected when it does not match request host
- **WHEN** a `Set-Cookie` specifies a `Domain` that does not domain-match the request URI host
- **THEN** the cookie is rejected (not stored)

#### Scenario: Path matching uses prefix with separator
- **WHEN** a cookie has `Path=/foo`
- **THEN** it matches `/foo`, `/foo/bar`, but not `/foobar`

#### Scenario: IP address hosts disable domain matching
- **WHEN** the request host is an IP address
- **THEN** subdomain-style domain matching is not applied; only exact host match is used

### Requirement: Cookie ordering on injection

Applicable cookies are sorted before injection into the `Cookie` header, following RFC 6265 section 5.4 ordering.

#### Scenario: Longer paths come first, ties broken by creation time
- **WHEN** multiple cookies match a request
- **THEN** they are ordered by longest path first, then by earliest creation time

### Requirement: SameSite enforcement (RFC 6265bis section 5.4, 5.8.3)

Cross-site requests filter cookies based on the `SameSite` attribute relative to the first-party context.

#### Scenario: SameSite=Strict cookies are omitted on cross-site requests
- **WHEN** a request is cross-site and a cookie has `SameSite=Strict`
- **THEN** the cookie is not sent

#### Scenario: SameSite=Lax cookies are sent on safe cross-site navigations
- **WHEN** a request is cross-site with a safe method (GET/HEAD) and a cookie has `SameSite=Lax`
- **THEN** the cookie is sent

#### Scenario: SameSite=Lax cookies are omitted on unsafe cross-site requests
- **WHEN** a request is cross-site with an unsafe method (POST) and a cookie has `SameSite=Lax`
- **THEN** the cookie is not sent

#### Scenario: SameSite=None cookies are always sent
- **WHEN** a cookie has `SameSite=None`
- **THEN** it is sent on both same-site and cross-site requests

#### Scenario: Absent SameSite is treated as permissive
- **WHEN** no `SameSite` attribute is present (`Unspecified`)
- **THEN** the cookie is sent in all contexts (same-site and cross-site)

#### Scenario: Same-site determination uses registrable domain
- **WHEN** determining whether a request is same-site or cross-site
- **THEN** the comparison uses a last-two-labels registrable domain approximation (no Public Suffix List)

### Requirement: Secure and HttpOnly attributes

#### Scenario: Secure cookie is only sent over HTTPS
- **WHEN** a cookie has the `Secure` flag
- **THEN** it is included only in requests with an `https` scheme

#### Scenario: HttpOnly attribute is stored
- **WHEN** a cookie has the `HttpOnly` flag
- **THEN** the flag is persisted in the store entry (enforcement is at the script-access boundary, not in the jar)

### Requirement: Cookie name prefix enforcement (RFC 6265bis section 4.1.3)

#### Scenario: __Host- prefix requires Secure, no Domain, Path=/
- **WHEN** a cookie name starts with `__Host-`
- **THEN** it is rejected unless `Secure` is set, no `Domain` attribute is present, and `Path` is exactly `/`

#### Scenario: __Secure- prefix requires Secure
- **WHEN** a cookie name starts with `__Secure-`
- **THEN** it is rejected unless the `Secure` attribute is set

### Requirement: Expires parsing tolerance

The parser accepts multiple date formats for the `Expires` attribute, including RFC 1123, RFC 850, two-digit year variants, and a generic fallback parse.

#### Scenario: Standard and legacy date formats are accepted
- **WHEN** an `Expires` value is in any of the recognized RFC or legacy formats
- **THEN** the value is parsed successfully and the cookie has a valid expiry

---

## Alt-Svc

### Requirement: Alt-Svc header parsing (RFC 7838 section 3)

The `AltSvcParser` parses an `Alt-Svc` header value into a list of `AltSvcEntry` records, each representing one alternative service directive.

#### Scenario: Single alternative is parsed
- **WHEN** the header is `h3=":443"; ma=86400`
- **THEN** one entry is produced with protocol `h3`, empty host (same-origin), port 443, max-age 86400

#### Scenario: Multiple alternatives are parsed
- **WHEN** the header is `h3=":443", h2=":443"`
- **THEN** two entries are produced, one for each protocol

#### Scenario: Default max-age is 24 hours
- **WHEN** the `ma` parameter is absent
- **THEN** `MaxAge` defaults to 86400 seconds per RFC 7838 section 3.1

#### Scenario: Persist flag is recognized
- **WHEN** the header contains `persist=1`
- **THEN** the entry's `Persist` property is `true`

#### Scenario: "clear" invalidates cached alternatives
- **WHEN** the header value is `clear`
- **THEN** the parser returns an empty list and signals `isClear = true`

#### Scenario: Malformed alternatives are skipped
- **WHEN** an individual alternative in a comma-separated list is unparseable
- **THEN** it is silently skipped and valid alternatives in the same header are still returned

#### Scenario: Authority with explicit host is parsed
- **WHEN** the authority is `"alt.example.com:443"` (with host)
- **THEN** the entry's `Host` is `alt.example.com` and `Port` is 443

#### Scenario: Port validation rejects out-of-range values
- **WHEN** the port in the authority is 0 or greater than 65535
- **THEN** the alternative is rejected

### Requirement: Alt-Svc cache with TTL-based expiration

`AltSvcCache` is a thread-safe, per-host cache of parsed Alt-Svc entries. It supports storing, looking up, and evicting entries based on their computed expiry.

#### Scenario: Entries are stored and retrievable per host
- **WHEN** `Store` is called with entries for a host
- **THEN** `TryGetHttp3` can find an HTTP/3 entry for that host

#### Scenario: Store replaces previous entries for same host
- **WHEN** `Store` is called for a host that already has cached entries
- **THEN** the old entries are replaced entirely

#### Scenario: Expired entries are not returned
- **WHEN** all cached entries for a host have passed their `ExpiresAt`
- **THEN** `TryGetHttp3` returns false and the expired entries are evicted

#### Scenario: Clear removes entries for a single host
- **WHEN** `Clear` is called with a host name
- **THEN** only that host's entries are removed; other hosts are unaffected

#### Scenario: ClearAll removes all entries
- **WHEN** `ClearAll` is called
- **THEN** the cache is empty

#### Scenario: TryGetHttp3 selects only h3 protocol entries
- **WHEN** the cache contains entries with protocols `h2` and `h3` for the same host
- **THEN** `TryGetHttp3` returns the `h3` entry

#### Scenario: Concurrent access is safe
- **WHEN** multiple threads call `Store`, `TryGetHttp3`, and `Clear` concurrently
- **THEN** no data corruption or exceptions occur (backed by ConcurrentDictionary)

---

## Diagnostics

### Requirement: Servus.Senf trace bridge to Microsoft.Extensions.Logging

`LoggerTraceListener` implements `IServusTraceListener` and forwards internal trace events to the standard `ILoggerFactory` pipeline, mapping Servus trace levels to `Microsoft.Extensions.Logging.LogLevel`.

#### Scenario: Trace level mapping
- **WHEN** a `TraceEvent` is emitted at a given Servus `TraceLevel`
- **THEN** it is forwarded to `ILogger` at the corresponding `LogLevel` (Trace->Trace, Debug->Debug, Info->Information, Warning->Warning, Error->Error)

#### Scenario: Per-category logger creation
- **WHEN** events arrive from different trace categories (e.g., `Protocol`, `Stage`)
- **THEN** each category gets its own `ILogger` instance named `GaudiHTTP.Trace.{Category}`

#### Scenario: Concurrent event writing is safe
- **WHEN** trace events arrive from multiple dispatcher and IO threads simultaneously
- **THEN** logger lookup uses `ConcurrentDictionary` to avoid dictionary corruption

#### Scenario: IsEnabled delegates to underlying logger
- **WHEN** `IsEnabled` is called with a level and category
- **THEN** it checks the corresponding `ILogger.IsEnabled` with the mapped log level

### Requirement: DI registration for tracing

Two extension methods register the trace listener with the DI container and configure the global Servus tracing sink.

#### Scenario: AddGaudiLoggerTracing creates a LoggerTraceListener from DI
- **WHEN** `AddGaudiLoggerTracing` is called on `IServiceCollection`
- **THEN** a `LoggerTraceListener` is registered as singleton, backed by the container's `ILoggerFactory`, with the specified minimum level and optional category filter

#### Scenario: AddGaudiTracing registers a caller-supplied listener
- **WHEN** `AddGaudiTracing` is called with a custom `IServusTraceListener`
- **THEN** the listener is registered as singleton and configured as the global Servus trace sink

### Requirement: OpenTelemetry integration

Extension methods wire GaudiHTTP's activity sources and meters into the OpenTelemetry SDK.

#### Scenario: AddGaudiHttpInstrumentation registers client tracing
- **WHEN** called on `TracerProviderBuilder`
- **THEN** the Servus activity source is added so client request spans are exported

#### Scenario: AddGaudiHttpInstrumentation registers client metrics
- **WHEN** called on `MeterProviderBuilder`
- **THEN** the Servus meter is added so client metric instruments are exported

#### Scenario: AddGaudiServerInstrumentation registers server tracing and metrics
- **WHEN** called on `TracerProviderBuilder` or `MeterProviderBuilder`
- **THEN** the Servus activity source or meter is added for server-side telemetry export

### Requirement: Client metrics emission points

The client pipeline emits counters, histograms, and up-down counters through `ServusMetrics` for request lifecycle and feature-specific events.

#### Scenario: Request count is incremented per request
- **WHEN** a client HTTP request is sent
- **THEN** `http.client.request.count` counter is incremented

#### Scenario: Request duration is recorded
- **WHEN** a client HTTP request completes
- **THEN** `http.client.request.duration` histogram records the elapsed time in seconds

#### Scenario: Active requests gauge tracks in-flight requests
- **WHEN** requests start and complete
- **THEN** `http.client.active_requests` up-down counter reflects the current in-flight count

#### Scenario: Cache, retry, and redirect counters are incremented on feature use
- **WHEN** a cache lookup, retry attempt, or redirect hop occurs
- **THEN** the corresponding counter (`http.client.cache.lookup`, `http.client.retry.count`, `http.client.redirect.count`) is incremented

### Requirement: Server metrics emission points

The server pipeline emits metrics covering connection lifecycle, request processing, TLS, and pipeline state.

#### Scenario: Active connections and connection duration are tracked
- **WHEN** server connections open and close
- **THEN** `gaudi.server.active_connections` reflects the current count and `gaudi.server.connection.duration` records the elapsed time

#### Scenario: TLS handshake metrics are recorded
- **WHEN** a TLS handshake occurs
- **THEN** `gaudi.server.tls_handshake.duration` records the elapsed time and `gaudi.server.active_tls_handshakes` tracks in-progress handshakes

#### Scenario: Server request metrics follow OTel HTTP semantic conventions
- **WHEN** server requests are processed
- **THEN** `http.server.active_requests` and `http.server.request.duration` are recorded

#### Scenario: Pipeline backpressure state is observable
- **WHEN** requests are being processed and responses are buffered
- **THEN** `gaudi.server.pipeline.inflight` and `gaudi.server.pipeline.pending` reflect current counts

#### Scenario: Handler timeouts and drain state are counted
- **WHEN** an application handler times out or connections are draining during shutdown
- **THEN** `gaudi.server.handler.timeouts` and `gaudi.server.drain.active` reflect the events

### Requirement: Client distributed tracing spans

The client creates `Activity` spans following OpenTelemetry HTTP semantic conventions, with W3C trace context propagation.

#### Scenario: Client request span is created with standard tags
- **WHEN** a client request is started with tracing active
- **THEN** an `Activity` named `GaudiHTTP.ClientRequest` of kind `Client` is created with tags `http.request.method`, `url.full`, `url.scheme`, `server.address`, `server.port`

#### Scenario: Non-standard HTTP methods are normalized to _OTHER
- **WHEN** the request method is not one of the nine standard methods (GET, HEAD, POST, PUT, DELETE, CONNECT, OPTIONS, TRACE, PATCH)
- **THEN** `http.request.method` is set to `_OTHER` and `http.request.method_original` preserves the actual method

#### Scenario: URL query strings are redacted
- **WHEN** the request URL contains a query string
- **THEN** `url.full` replaces the query with `?*` to avoid leaking sensitive parameters

#### Scenario: Response status and errors are recorded on the span
- **WHEN** a response is received or an exception occurs
- **THEN** `http.response.status_code` and `network.protocol.version` are set; status codes >= 400 set `error.type` and `ActivityStatusCode.Error`

#### Scenario: W3C trace context is propagated via request headers
- **WHEN** `InjectTraceContext` is called
- **THEN** `DistributedContextPropagator.Current` injects traceparent/tracestate into the request headers

#### Scenario: Feature events are recorded as Activity events
- **WHEN** a redirect, retry, or cache lookup occurs during a traced request
- **THEN** corresponding `ActivityEvent` instances (`http.redirect`, `http.retry`, `http.cache_lookup`) are added to the span

### Requirement: Server distributed tracing spans

The server creates connection-level and request-level `Activity` spans, with inbound trace context extraction.

#### Scenario: Connection activity is created
- **WHEN** a new server connection is accepted with tracing active
- **THEN** an `Activity` named `GaudiHTTP.Connection` of kind `Server` is created with `server.address`, `server.port`, and `network.transport` tags

#### Scenario: Request activity extracts inbound trace context
- **WHEN** a server request carries `traceparent` and optional `tracestate` headers
- **THEN** the created `GaudiHTTP.ServerRequest` activity uses the parsed context as its parent

#### Scenario: Backpressure events are recorded on server spans
- **WHEN** the server pipeline applies backpressure
- **THEN** a `gaudi.backpressure` event is added with `gaudi.pipeline.inflight` and `gaudi.pipeline.max` tags
