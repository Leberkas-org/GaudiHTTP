# Public API Surface

The public API surface of GaudiHTTP encompasses the client-side types (IGaudiHttpClient, factory,
builder, options, handlers) and server-side types (GaudiServer, options, listen configuration,
ASP.NET Core feature adapters). All public types follow the extend-only design principle: new
members may be added, but existing signatures, defaults, and wire-format semantics must not change
in ways that break existing consumers.

## Requirements

### Requirement: IGaudiHttpClient contract

`IGaudiHttpClient` is the primary client interface. It exposes both a conventional `SendAsync`
method for single-request use and a `System.Threading.Channels`-based API (`Requests`/`Responses`)
for high-throughput streaming scenarios. It implements `IDisposable`.

#### Scenario: SendAsync sends a request and returns the response
- **WHEN** `SendAsync` is called with an `HttpRequestMessage` and `CancellationToken`
- **THEN** the request is written to the internal `Requests` channel
- **AND** the method awaits the matching `HttpResponseMessage`
- **AND** the per-client `Timeout` is applied unless the caller supplies a per-request timeout

#### Scenario: Channel-based API for high-throughput
- **WHEN** a consumer writes an `HttpRequestMessage` to `Requests`
- **THEN** the pipeline processes it asynchronously
- **AND** the response is readable from `Responses`
- **AND** `HttpResponseMessage.RequestMessage` identifies the originating request

#### Scenario: CancelPendingRequests cancels all in-flight work
- **WHEN** `CancelPendingRequests` is called
- **THEN** all pending `SendAsync` tasks are cancelled
- **AND** stale responses in the `Responses` channel are drained and disposed

#### Scenario: Dispose releases resources and rejects new requests
- **WHEN** `Dispose` is called
- **THEN** the consumer registration is released
- **AND** all pending requests are cancelled
- **AND** subsequent `SendAsync` calls throw `ObjectDisposedException`

#### Scenario: Mutable per-instance defaults
- **WHEN** `BaseAddress`, `DefaultRequestVersion`, `DefaultVersionPolicy`, or `Timeout` is set
- **THEN** the cached options snapshot is updated
- **AND** subsequent requests use the new values
- **AND** in-flight requests are not affected (options are captured at submission time)

#### Scenario: Default request headers
- **WHEN** headers are added to `DefaultRequestHeaders`
- **THEN** they are included on every outgoing request from this client instance

---

### Requirement: Client factory

`IGaudiHttpClientFactory` creates named `IGaudiHttpClient` instances. It is the entry point for
obtaining client handles in DI-based applications and mirrors the `IHttpClientFactory` pattern.

#### Scenario: Named client creation
- **WHEN** `CreateClient(name)` is called
- **THEN** a new `IGaudiHttpClient` is returned configured with the options registered for that name
- **AND** each call returns a fresh client handle with its own mutable state

#### Scenario: Default (unnamed) client
- **WHEN** `CreateClient()` (the extension method) is called with no arguments
- **THEN** it delegates to `CreateClient(string.Empty)`

#### Scenario: Factory is singleton
- **WHEN** the factory is resolved from DI
- **THEN** a single `IGaudiHttpClientFactory` instance is shared across the application
- **AND** it owns the Akka ActorSystem used for all client pipelines

#### Scenario: Factory disposal
- **WHEN** the factory is disposed
- **THEN** the internal stream manager is shut down
- **AND** subsequent `CreateClient` calls throw `ObjectDisposedException`

---

### Requirement: Client builder and DI registration

`IGaudiHttpClientBuilder` provides a fluent API for configuring a named client during service
registration. The `AddGaudiHttpClient` extension methods on `IServiceCollection` return builder
instances.

#### Scenario: Named client registration
- **WHEN** `services.AddGaudiHttpClient("api", o => { ... })` is called
- **THEN** `GaudiClientOptions` are registered for the name "api"
- **AND** `IGaudiHttpClientFactory` is registered as a singleton (idempotent)
- **AND** the returned `IGaudiHttpClientBuilder` has `Name == "api"`

#### Scenario: Default client registration
- **WHEN** `services.AddGaudiHttpClient(o => { ... })` is called without a name
- **THEN** the client is registered with `string.Empty` as the name

#### Scenario: Typed client registration (single type)
- **WHEN** `services.AddGaudiHttpClient<TClient>(o => { ... })` is called
- **THEN** `TClient` is registered as Transient, resolved via the factory
- **AND** the client name is `typeof(TClient).Name`

#### Scenario: Typed client registration (interface + implementation)
- **WHEN** `services.AddGaudiHttpClient<TClient, TImpl>(o => { ... })` is called
- **THEN** both `TClient` and `TImpl` are registered as Transient
- **AND** both are resolved via `IGaudiHttpClientFactory` with `ActivatorUtilities`

---

### Requirement: Client builder extensions (pipeline features)

Fluent extension methods on `IGaudiHttpClientBuilder` configure pipeline stages. Each feature is
opt-in. Registration order of handlers is preserved (FIFO).

#### Scenario: Cookie handling
- **WHEN** `builder.WithCookies()` is called
- **THEN** cookie handling is enabled with an in-memory `CookieJar`
- **WHEN** `builder.WithCookies(store)` is called with a custom `ICookieStore`
- **THEN** cookie handling uses the provided store

#### Scenario: Response caching
- **WHEN** `builder.WithCache()` is called
- **THEN** response caching is enabled with an in-memory store
- **AND** defaults are: 1000 max entries, 50 MiB max body size, 256 MiB max total size, private cache mode

#### Scenario: Automatic retries
- **WHEN** `builder.WithRetry()` is called
- **THEN** automatic retries are enabled with default policy (3 max retries, respects Retry-After)

#### Scenario: Automatic redirects
- **WHEN** `builder.WithRedirect()` is called
- **THEN** redirect following is enabled (max 10 redirects, HTTPS-to-HTTP downgrade blocked)

#### Scenario: Response decompression
- **WHEN** `builder.WithDecompression()` is called
- **THEN** automatic decompression of gzip/deflate/br response bodies is enabled

#### Scenario: Request compression
- **WHEN** `builder.WithRequestCompression()` is called
- **THEN** request bodies above 1024 bytes are compressed with gzip by default

#### Scenario: Expect 100-Continue
- **WHEN** `builder.WithExpectContinue()` is called
- **THEN** requests with body size above 1024 bytes send the `Expect: 100-continue` header

#### Scenario: Custom handler registration
- **WHEN** `builder.AddHandler<T>()` is called where T : GaudiHandler
- **THEN** `T` is registered as Transient and appended to the handler pipeline
- **AND** multiple handlers execute in FIFO registration order

#### Scenario: Inline request/response transforms
- **WHEN** `builder.UseRequest(transform)` or `builder.UseResponse(transform)` is called
- **THEN** the delegate is wrapped in an anonymous `GaudiHandler` and appended to the pipeline

---

### Requirement: GaudiHandler base class

`GaudiHandler` is the base class for pipeline middleware. It provides virtual methods for request
and response transformation.

#### Scenario: Default passthrough behavior
- **WHEN** a `GaudiHandler` subclass does not override `ProcessRequest` or `ProcessResponse`
- **THEN** the request and response pass through unchanged

#### Scenario: Request transformation
- **WHEN** `ProcessRequest(request)` is overridden
- **THEN** the returned `HttpRequestMessage` replaces the original in the pipeline

#### Scenario: Response transformation
- **WHEN** `ProcessResponse(original, response)` is overridden
- **THEN** the returned `HttpResponseMessage` replaces the original in the pipeline
- **AND** the original request is provided for correlation

---

### Requirement: Client options hierarchy

`GaudiClientOptions` is the top-level configuration class. It contains per-protocol sub-options
(`Http1`, `Http2`, `Http3`) and shared transport, TLS, proxy, pool, and body buffering settings.
Options follow a cascading inheritance model: per-protocol values override per-direction values
which override global values.

#### Scenario: Body buffering cascade
- **WHEN** a per-protocol `MaxBufferedRequestBodySize` is set (e.g. `Http2.MaxBufferedRequestBodySize`)
- **THEN** it takes precedence over `GaudiClientOptions.MaxBufferedRequestBodySize`
- **WHEN** `GaudiClientOptions.MaxBufferedRequestBodySize` is set
- **THEN** it takes precedence over `GaudiClientOptions.MaxBufferedBodySize`
- **WHEN** neither per-protocol nor per-direction overrides are set
- **THEN** `GaudiClientOptions.MaxBufferedBodySize` (default 64 KiB) is used

#### Scenario: Connection timeouts
- **WHEN** `ConnectTimeout` is configured
- **THEN** TCP/QUIC connection establishment is bounded by that duration (default 15s)
- **WHEN** `DefaultRequestTimeout` is configured
- **THEN** it applies as the default `IGaudiHttpClient.Timeout` (default 60s)

#### Scenario: Connection pool settings
- **WHEN** `PooledConnectionIdleTimeout` is configured (default 90s)
- **THEN** idle connections are evicted after that duration
- **WHEN** `PooledConnectionLifetime` is configured (default infinite)
- **THEN** connections exceeding that age are not reused

#### Scenario: TLS configuration
- **WHEN** `EnabledSslProtocols` is set to `SslProtocols.None` (default)
- **THEN** the OS selects the best available TLS version
- **WHEN** `DangerousAcceptAnyServerCertificate` is true
- **THEN** all server certificates are accepted (dev/test only)
- **WHEN** `ServerCertificateValidationCallback` is set
- **THEN** it is used for certificate validation unless `DangerousAcceptAnyServerCertificate` overrides it
- **WHEN** `ClientCertificates` is set
- **THEN** the certificates are presented during the TLS handshake

#### Scenario: Proxy configuration
- **WHEN** `UseProxy` is true and `Proxy` is set
- **THEN** requests are routed through the configured proxy
- **AND** HTTP/3 requests are downgraded to HTTP/2 through the proxy (QUIC cannot traverse HTTP proxies)

#### Scenario: Stream retry configuration
- **WHEN** the Akka Streams pipeline fails to materialize
- **THEN** retries use exponential backoff from `StreamRetryInitialBackoff` (100ms) to `StreamRetryMaxBackoff` (30s)
- **AND** backoff multiplier is `StreamRetryBackoffMultiplier` (2.0) with jitter `StreamRetryBackoffJitter` (0.2)
- **AND** maximum attempts is `MaxStreamRetryAttempts` (10)

#### Scenario: Per-protocol connection limits
- **WHEN** `Http1.MaxConnectionsPerServer` is set (default 6)
- **THEN** at most that many TCP connections are opened per server for H1.x
- **WHEN** `Http2.MaxConnectionsPerServer` is set (default 6)
- **THEN** at most that many TCP connections are opened per server for H2
- **WHEN** `Http3.MaxConnectionsPerServer` is set (default 4)
- **THEN** at most that many QUIC connections are opened per server for H3

#### Scenario: H2 flow control settings
- **WHEN** `Http2.InitialConnectionWindowSize` is set (default 64 MiB)
- **THEN** the H2 connection-level flow-control window is advertised at that size
- **WHEN** `Http2.InitialStreamWindowSize` is set (default 1 MiB)
- **THEN** the H2 per-stream initial window is advertised at that size
- **WHEN** `Http2.EnableAdaptiveWindowScaling` is true (default)
- **THEN** per-stream windows grow up to `Http2.MaxStreamWindowSize` (16 MiB) based on measured throughput

#### Scenario: H2 keep-alive pings
- **WHEN** `Http2.KeepAlivePingDelay` is not infinite
- **THEN** PING frames are sent after that idle period
- **AND** if no frame is received within `KeepAlivePingTimeout` (20s), the connection is closed
- **AND** `KeepAlivePingPolicy` controls whether pings are sent always or only with active requests

#### Scenario: H3 QPACK settings
- **WHEN** `Http3.QpackMaxTableCapacity` is set (default 16 KiB)
- **THEN** that capacity is advertised to the server
- **WHEN** `Http3.QpackBlockedStreams` is set (default 100)
- **THEN** that many streams may be blocked waiting for QPACK encoder instructions

#### Scenario: H3 Alt-Svc discovery
- **WHEN** `Http3.EnableAltSvcDiscovery` is true
- **THEN** Alt-Svc headers in H1/H2 responses are parsed and cached per-host
- **AND** subsequent requests to that host are upgraded to H3 if QUIC is available

#### Scenario: Reconnect configuration (all protocols)
- **WHEN** a connection drops with in-flight requests
- **THEN** up to `MaxReconnectAttempts` (default 3) reconnection attempts are made per protocol
- **AND** retry delays use exponential backoff with jitter

---

### Requirement: GaudiServer as IServer

`GaudiServer` implements `Microsoft.AspNetCore.Hosting.Server.IServer` and serves as the ASP.NET
Core server implementation. It manages an Akka ActorSystem, resolves configured endpoints, and
routes incoming connections through the application pipeline.

#### Scenario: Server startup
- **WHEN** `StartAsync` is called with an `IHttpApplication<TContext>`
- **THEN** server options are validated
- **AND** an ActorSystem is created if none is registered in DI
- **AND** configured endpoints are resolved and bound
- **AND** `IServerAddressesFeature` is populated with the bound addresses

#### Scenario: Server uses existing ActorSystem
- **WHEN** an `ActorSystem` is available in the DI container
- **THEN** the server reuses it instead of creating a new one
- **AND** the server does not dispose the shared ActorSystem

#### Scenario: Graceful shutdown
- **WHEN** `StopAsync` is called
- **THEN** the server stops accepting new connections
- **AND** in-flight requests are drained within `GracefulShutdownTimeout` (default 30s)
- **AND** if the server owns the ActorSystem, coordinated shutdown phases execute

#### Scenario: Shutdown drain timeout
- **WHEN** the drain does not complete within `GracefulShutdownTimeout`
- **THEN** a warning is logged
- **AND** the server proceeds with shutdown (does not hang indefinitely)

#### Scenario: Startup timeout
- **WHEN** listener binding does not complete within `StartupTimeout` (default 25s)
- **THEN** an `InvalidOperationException` is thrown
- **AND** the server fails to start

#### Scenario: Handler timeout
- **WHEN** a request handler does not complete within `HandlerTimeout` (default 30s)
- **THEN** its cancellation token is cancelled
- **AND** `HandlerGracePeriod` (default 5s) of additional time is granted for cleanup

---

### Requirement: Server hosting integration

`GaudiServerWebHostBuilderExtensions.UseGaudiHttp` registers GaudiServer as the ASP.NET Core
`IServer` implementation, replacing any previously registered server (e.g. Kestrel).

#### Scenario: UseGaudiHttp replaces the server
- **WHEN** `builder.UseGaudiHttp()` is called on an `IHostBuilder`
- **THEN** any previously registered `IServer` is removed
- **AND** `GaudiServer` is registered as a singleton `IServer`

#### Scenario: UseGaudiHttp with options
- **WHEN** `builder.UseGaudiHttp(o => { ... })` is called
- **THEN** `GaudiServerOptions` is configured via the provided delegate

---

### Requirement: Server options and limits

`GaudiServerOptions` controls server-wide timeouts, body settings, protocol sub-options, and
endpoint bindings. `GaudiServerLimits` controls connection and request limits.

#### Scenario: Connection limits
- **WHEN** `Limits.MaxConcurrentConnections` is set (default 0 = unlimited)
- **THEN** the server enforces that connection ceiling

#### Scenario: Request body limits
- **WHEN** `Limits.MaxRequestBodySize` is set (default ~28.6 MiB)
- **THEN** requests with bodies exceeding that size are rejected
- **AND** per-protocol overrides (`Http1.MaxRequestBodySize`, `Http2.MaxRequestBodySize`, `Http3.MaxRequestBodySize`) take precedence

#### Scenario: Header limits
- **WHEN** `Limits.MaxRequestHeaderCount` is set (default 100)
- **THEN** requests with more headers are rejected
- **WHEN** `Limits.MaxRequestHeadersTotalSize` is set (default 32 KiB)
- **THEN** requests with larger combined header size are rejected

#### Scenario: Keep-alive and timeouts
- **WHEN** `Limits.KeepAliveTimeout` is set (default 130s)
- **THEN** idle connections are closed after that duration
- **WHEN** `Limits.RequestHeadersTimeout` is set (default 30s)
- **THEN** connections that do not send complete headers in time are closed

#### Scenario: Data rate enforcement
- **WHEN** `Limits.MinRequestBodyDataRate` is set (default 240 bytes/sec)
- **THEN** connections sending body data below that rate are closed after the grace period
- **WHEN** `Limits.MinResponseDataRate` is set (default 240 bytes/sec)
- **THEN** connections reading response data below that rate are closed after the grace period

#### Scenario: Rapid Reset mitigation (CVE-2023-44487)
- **WHEN** more than `Limits.MaxResetStreamsPerWindow` (default 200) stream resets occur within `Limits.RapidResetDetectionWindow` (default 30s)
- **THEN** the connection is closed with GOAWAY(ENHANCE_YOUR_CALM)

#### Scenario: Response buffer backpressure
- **WHEN** `Limits.MaxResponseBufferSize` is set (default 64 KiB)
- **THEN** the output pipe applies backpressure when that many bytes are buffered

#### Scenario: Protocol sniffing limit
- **WHEN** `Limits.MaxProtocolSniffBytes` is set (default 64 KiB, minimum 24)
- **THEN** cleartext protocol detection (H1 vs h2c) buffers at most that many bytes

#### Scenario: Body buffering cascade (server)
- **WHEN** per-protocol body options are set (e.g. `Http2.MaxBufferedRequestBodySize`)
- **THEN** they override per-direction overrides (`MaxBufferedRequestBodySize`)
- **WHEN** per-direction overrides are set
- **THEN** they override the global `MaxBufferedBodySize` (default 64 KiB)

#### Scenario: Options validation on startup
- **WHEN** the server starts
- **THEN** all limit values are validated (non-negative sizes, positive timeouts, valid ranges)
- **AND** invalid values throw `ArgumentOutOfRangeException`
- **AND** H2 frame size is validated to be in [16 KiB, 16 MiB - 1]

---

### Requirement: Server listen options

`GaudiListenOptions` configures a single server endpoint: IP address, port, protocol selection,
and TLS. `GaudiServerOptions` provides multiple binding methods.

#### Scenario: URL-based binding
- **WHEN** `options.Listen("https://0.0.0.0:443")` is called
- **THEN** a listen endpoint is parsed and added with the appropriate scheme and port

#### Scenario: IP + port binding
- **WHEN** `options.Listen(IPAddress.Any, 8080)` is called
- **THEN** a listen endpoint on all interfaces at port 8080 is added

#### Scenario: Convenience binding methods
- **WHEN** `options.ListenLocalhost(5000)` is called
- **THEN** a listen endpoint on `127.0.0.1:5000` is added
- **WHEN** `options.ListenAnyIP(8080)` is called
- **THEN** a listen endpoint on `0.0.0.0:8080` is added

#### Scenario: TCP and QUIC transport binding
- **WHEN** `options.Bind(tcpListenerOptions)` is called
- **THEN** a TCP listener binding is added
- **WHEN** `options.Bind(quicListenerOptions)` is called
- **THEN** a QUIC listener binding is added

#### Scenario: Protocol selection
- **WHEN** `GaudiListenOptions.Protocols` is set (default `Http1AndHttp2`)
- **THEN** only the specified protocols are negotiated on that endpoint
- **AND** valid values are `Http1`, `Http2`, `Http1AndHttp2`, `Http3`

#### Scenario: Endpoint defaults callback
- **WHEN** `options.ConfigureEndpointDefaults(configure)` is called
- **THEN** the callback is applied to every endpoint's `GaudiListenOptions` before binding

#### Scenario: Transport buffer options
- **WHEN** `GaudiListenOptions.Transport` is set
- **THEN** custom backpressure thresholds are applied to the read/write pipes for that endpoint
- **WHEN** `Transport` is null
- **THEN** protocol-specific defaults are used (TCP: 64 KiB pause, QUIC: 4 KiB receive hint)

#### Scenario: Connection logging
- **WHEN** `listenOptions.UseConnectionLogging()` is called
- **THEN** per-connection logging is enabled under the default category
- **WHEN** `listenOptions.UseConnectionLogging("Custom.Category")` is called
- **THEN** per-connection logging uses the specified logger category

---

### Requirement: HTTPS / TLS configuration

`GaudiHttpsOptions` configures TLS for a server endpoint. `GaudiListenOptions.UseHttps` has
multiple overloads for certificate provisioning.

#### Scenario: Certificate from X509Certificate2
- **WHEN** `listenOptions.UseHttps(certificate)` is called
- **THEN** TLS is enabled with the provided in-memory certificate

#### Scenario: Certificate from file
- **WHEN** `listenOptions.UseHttps(path, password)` is called
- **THEN** TLS is enabled by loading the certificate from the file system

#### Scenario: Certificate selector (SNI)
- **WHEN** `GaudiHttpsOptions.ServerCertificateSelector` is set
- **THEN** the callback is invoked per-connection with the SNI hostname
- **AND** it takes precedence over `ServerCertificate`
- **AND** it is not supported for HTTP/3 (QUIC) endpoints

#### Scenario: Client certificate modes
- **WHEN** `GaudiHttpsOptions.ClientCertificateMode` is set to `NoCertificate` (default)
- **THEN** no client certificate is requested
- **WHEN** set to other modes
- **THEN** client certificates are requested according to the mode
- **AND** `ClientCertificateValidationCallback` is invoked for validation

#### Scenario: TLS handshake timeout
- **WHEN** `GaudiHttpsOptions.HandshakeTimeout` is configured (default 10s)
- **THEN** TLS handshakes that exceed the timeout are aborted

#### Scenario: HTTPS defaults callback
- **WHEN** `options.ConfigureHttpsDefaults(configure)` is called
- **THEN** the callback is applied to every HTTPS endpoint's `GaudiHttpsOptions` before binding

---

### Requirement: HttpProtocols flags

`HttpProtocols` is a flags enumeration controlling which HTTP versions a server endpoint negotiates.

#### Scenario: ALPN negotiation
- **WHEN** `HttpProtocols.Http1AndHttp2` is configured on a TLS endpoint
- **THEN** ALPN advertises both `h2` and `http/1.1`, preferring H2
- **WHEN** `HttpProtocols.Http3` is configured
- **THEN** ALPN advertises `h3` (requires QUIC transport)

---

### Requirement: ASP.NET Core feature adapters

The server exposes ASP.NET Core HTTP features via `IFeatureCollection`. Feature objects are pooled
per-connection (H1) or per-stream (H2/H3) and reset between requests to minimize allocations.

#### Scenario: Core feature set
- **WHEN** a request is dispatched to the application
- **THEN** the feature collection contains:
  - `IHttpRequestFeature` (method, path, query, headers, body, protocol, scheme)
  - `IHttpResponseFeature` (status code, reason phrase, headers, body)
  - `IHttpResponseBodyFeature` (write pipe, SendFile, stream)
  - `IHttpConnectionFeature` (local/remote addresses and ports)
  - `IHttpRequestBodyDetectionFeature` (whether a request body is present)
  - `IHttpRequestLifetimeFeature` (request abort signaling)
  - `IHttpRequestIdentifierFeature` (unique request ID)
  - `IHttpResponseTrailersFeature` (response trailers for H2/H3)
  - `IHttpRequestTrailersFeature` (request trailers)
  - `IHttpMaxRequestBodySizeFeature` (per-request body size limit)
  - `IHttpBodyControlFeature` (synchronous I/O control)

#### Scenario: Feature collection pooling
- **WHEN** a request completes
- **THEN** the feature collection and its sub-features are reset and returned to the pool
- **AND** recycled collections reuse existing feature objects (no re-allocation)

#### Scenario: TLS features
- **WHEN** the connection uses TLS
- **THEN** `ITlsHandshakeFeature` is set on the feature collection

#### Scenario: Stream ID feature (H2/H3)
- **WHEN** the request arrives on an H2 or H3 stream
- **THEN** `IHttpStreamIdFeature` is available with the stream identifier

#### Scenario: Reset feature (H2/H3)
- **WHEN** the request arrives on an H2 or H3 stream
- **THEN** `IHttpResetFeature` allows resetting the stream with an error code

---

### Requirement: Extend-only API design

All public types follow the extend-only design principle to maintain backward compatibility across
releases. This is critical for consumers who depend on the API surface.

#### Scenario: New members may be added
- **WHEN** a new release adds a property to `GaudiClientOptions` or `GaudiServerOptions`
- **THEN** it has a sensible default value
- **AND** existing consumers compile and behave identically without changes

#### Scenario: Existing signatures are stable
- **WHEN** a type or method is part of the public API
- **THEN** its signature (name, parameter types, return type) does not change
- **AND** its default behavior does not change

#### Scenario: Wire format compatibility
- **WHEN** a client or server is upgraded
- **THEN** it remains interoperable with older peers at the wire level
- **AND** no protocol-level defaults change in a way that breaks existing deployments

#### Scenario: Options defaults are stable
- **WHEN** a consumer relies on the default value of a configuration property
- **THEN** that default does not change in a minor or patch release
- **AND** changes to defaults are documented as breaking changes in major releases
