# VitePress Documentation Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Full overhaul of the TurboHTTP documentation site — fix content accuracy, correct the server narrative (standalone, not Kestrel), restructure navigation, add real-world scenarios, and redesign the homepage.

**Architecture:** Custom Vue homepage component + VitePress DefaultTheme for content pages. Navigation simplified from 7 to 4 top-level items. API reference split from 1 monolith into 6 focused pages. New "Getting Started" section absorbs quickstart + architecture overview.

**Tech Stack:** VitePress 1.6+, Vue 3 (SFC), TypeScript, CSS custom properties, LikeC4 (existing)

**Key context files:**
- Spec: `docs/superpowers/specs/2026-05-19-vitepress-redesign-design.md`
- VitePress config: `docs/.vitepress/config.ts`
- Theme: `docs/.vitepress/theme/index.ts`, `docs/.vitepress/theme/custom.css`
- Docs CLAUDE.md: `docs/CLAUDE.md` (content rules: no RFC refs, user-facing tone)

**Verified API surface (from source code — use these, NOT the current docs):**

```
ITurboHttpClient: BaseAddress, DefaultRequestHeaders, DefaultRequestVersion,
  DefaultVersionPolicy, Timeout, Requests, Responses, CancelPendingRequests(), SendAsync()
  NOTE: NO MaxResponseContentBufferSize property

TurboClientOptions: BaseAddress, Http1/2/3 (init), MaxBufferedBodySize (4MB),
  MaxStreamedBodySize (null), ConnectTimeout (15s), PooledConnectionIdleTimeout (90s),
  PooledConnectionLifetime (infinite), MaxEndpointSubstreams (256),
  SocketSendBufferSize, SocketReceiveBufferSize, TLS options, Proxy options, Credentials

Http1Options: MaxConnectionsPerServer (6), MaxPipelineDepth (16),
  MaxResponseHeadersLength (64 KB), AutoHost (true), AutoAcceptEncoding (true),
  MaxReconnectAttempts (3)
  NOTE: NO MaxBatchWeight property

Http2Options: MaxConnectionsPerServer (6), MaxConcurrentStreams (100),
  InitialConnectionWindowSize (64MB), InitialStreamWindowSize (2MB),
  MaxFrameSize (64KB), HeaderTableSize (64KB), MaxReconnectAttempts (3),
  KeepAlivePingDelay (infinite), KeepAlivePingTimeout (20s),
  KeepAlivePingPolicy (Always)

Http3Options: MaxConnectionsPerServer (4), MaxConcurrentStreams (100),
  QpackMaxTableCapacity (16KB), QpackBlockedStreams (100),
  MaxFieldSectionSize (64KB), IdleTimeout (30s), MaxReconnectAttempts (3),
  AllowConnectionMigration (true), EnableAltSvcDiscovery (false),
  MaxReconnectBufferSize (64)
  NOTE: NO AllowEarlyData, NO AllowServerPush properties

Builder extensions: WithCookies(), WithCache(), WithRetry(), WithRedirect(),
  WithDecompression(), WithRequestCompression(), WithExpectContinue(),
  AddHandler<T>(), UseRequest(), UseResponse()

TurboEntityBuilder: OnGet/Post/Put/Delete/Patch(), MapResponse<T>(),
  WithTimeout(), UseResolver(), UseResolver<T>(), UseActorRef<T>(),
  UseActorRef(Func<IServiceProvider, IActorRef>),
  UseActorRef(Func<IReadOnlyActorRegistry, IActorRef>)
  NOTE: NO WithEntityKey() method

MapTurboEntity overloads:
  MapTurboEntity(string pattern, Action<TurboEntityBuilder> configure)
  MapTurboEntity<TActorKey>(string pattern, Action<TurboEntityBuilder> configure)

Server is STANDALONE (Akka.Streams + Servus.Akka.Transport). NOT Kestrel-based.
AddTurboKestrel is named for configuration familiarity only.
```

---

## Phase 1: Foundation (Config + Directory Structure)

### Task 1: Update VitePress config with new navigation and sidebars

**Files:**
- Modify: `docs/.vitepress/config.ts`

- [ ] **Step 1: Replace the full config.ts with new navigation and sidebar structure**

```typescript
import { defineConfig } from 'vitepress'

export default defineConfig({
    title: 'TurboHTTP',
    description: 'High-performance HTTP client and server for .NET built on Akka.Streams — HTTP/1.0, HTTP/1.1, HTTP/2, and HTTP/3 (QUIC) with automatic retries, caching, cookies, connection pooling, middleware pipeline, routing, and entity gateway.',
    base: '/',
    head: [
        ['link', { rel: 'icon', type: 'image/png', href: '/logo/icon.png' }],
    ],
    themeConfig: {
        logo: '/logo/icon.png',
        search: {
            provider: 'local',
        },

        nav: [
            { text: 'Getting Started', link: '/getting-started/' },
            { text: 'Client', link: '/client/' },
            { text: 'Server', link: '/server/' },
            { text: 'API', link: '/api/' },
        ],

        sidebar: {
            '/getting-started/': [
                {
                    text: 'Getting Started',
                    items: [
                        { text: 'Overview', link: '/getting-started/' },
                        { text: 'Client Quick Start', link: '/getting-started/client' },
                        { text: 'Server Quick Start', link: '/getting-started/server' },
                        { text: 'Architecture Overview', link: '/getting-started/architecture' },
                        { text: 'Migration from HttpClient', link: '/getting-started/migration' },
                    ],
                },
            ],
            '/client/': [
                {
                    text: 'Client',
                    items: [
                        { text: 'Overview', link: '/client/' },
                        { text: 'Installation & Setup', link: '/client/installation' },
                        { text: 'Configuration', link: '/client/configuration' },
                        { text: 'Connection Pooling', link: '/client/connection-pooling' },
                        { text: 'Automatic Retries', link: '/client/retries' },
                        { text: 'HTTP Caching', link: '/client/caching' },
                        { text: 'Cookie Management', link: '/client/cookies' },
                        { text: 'Redirects', link: '/client/redirects' },
                        { text: 'Content Encoding', link: '/client/content-encoding' },
                        { text: 'HTTP/2 & Multiplexing', link: '/client/http2' },
                        { text: 'HTTP/3 & QUIC', link: '/client/http3' },
                        { text: 'Real-World Scenarios', link: '/client/scenarios' },
                        { text: 'Troubleshooting', link: '/client/troubleshooting' },
                    ],
                },
            ],
            '/server/': [
                {
                    text: 'Server',
                    items: [
                        { text: 'Overview', link: '/server/' },
                        { text: 'Installation & Setup', link: '/server/installation' },
                        { text: 'Configuration', link: '/server/configuration' },
                        { text: 'Hosting & Lifecycle', link: '/server/hosting' },
                        { text: 'Middleware Pipeline', link: '/server/middleware' },
                        { text: 'Routing', link: '/server/routing' },
                        { text: 'Parameter Binding', link: '/server/binding' },
                        { text: 'Validation', link: '/server/validation' },
                        { text: 'Entity Gateway', link: '/server/entity-gateway' },
                        { text: 'Real-World Scenarios', link: '/server/scenarios' },
                        { text: 'Troubleshooting', link: '/server/troubleshooting' },
                    ],
                },
            ],
            '/api/': [
                {
                    text: 'API Reference',
                    items: [
                        { text: 'Overview', link: '/api/' },
                        { text: 'Client API', link: '/api/client' },
                        { text: 'Client Options', link: '/api/client-options' },
                        { text: 'Feature Options', link: '/api/feature-options' },
                        { text: 'Server API', link: '/api/server' },
                        { text: 'Entity Gateway API', link: '/api/entity-gateway' },
                    ],
                },
            ],
            '/architecture/': [
                {
                    text: 'Architecture',
                    items: [
                        { text: 'Request Pipeline', link: '/architecture/pipeline' },
                        { text: 'Protocol Engines', link: '/architecture/engines' },
                        { text: 'Handler Design', link: '/architecture/handlers' },
                        { text: 'E2E Scenarios', link: '/architecture/scenarios' },
                        { text: 'Extending the Pipeline', link: '/architecture/extending' },
                    ],
                },
            ],
        },

        socialLinks: [
            { icon: 'github', link: 'https://github.com/leberkas-org/TurboHTTP' },
        ],

        footer: {
            message: 'Released under the MIT License.',
            copyright: 'Copyright © TurboHTTP Contributors',
        },
    },
})
```

- [ ] **Step 2: Verify the config parses correctly**

Run from `docs/`:
```bash
npx vitepress build 2>&1 | head -5
```
Expected: Build starts (will fail on missing pages — that's fine for now). No syntax errors in config.

- [ ] **Step 3: Commit**

```bash
git add docs/.vitepress/config.ts
git commit -m "docs: update VitePress config with new nav and sidebar structure"
```

---

### Task 2: Create getting-started directory with placeholder pages

**Files:**
- Create: `docs/getting-started/index.md`
- Create: `docs/getting-started/client.md`
- Create: `docs/getting-started/server.md`
- Create: `docs/getting-started/architecture.md`
- Create: `docs/getting-started/migration.md`

- [ ] **Step 1: Create the getting-started directory**

```bash
mkdir -p docs/getting-started
```

- [ ] **Step 2: Create placeholder index.md**

Write to `docs/getting-started/index.md`:

```markdown
# Getting Started

TurboHTTP is a high-performance HTTP client and server for .NET built on Akka.Streams. It supports HTTP/1.0, HTTP/1.1, HTTP/2, and HTTP/3 (QUIC) with automatic retries, caching, cookies, connection pooling, middleware, routing, and entity gateway — all in one package.

## Install

```bash
dotnet add package TurboHTTP
```

Or add it to your `.csproj`:

```xml
<PackageReference Include="TurboHTTP" Version="1.*" />
```

## Choose Your Path

TurboHTTP has two sides — use either or both:

| | Client | Server |
|---|---|---|
| **What it does** | Makes HTTP requests with built-in retries, caching, cookies, and connection pooling | Handles HTTP requests with middleware, routing, and actor-based entity gateway |
| **Get started** | [Client Quick Start →](./client) | [Server Quick Start →](./server) |
| **Full docs** | [Client Guide →](/client/) | [Server Guide →](/server/) |

## Quick Look

### Client

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboHttpClient("api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRetry()
.WithCache()
.WithCookies()
.WithRedirect();

var app = builder.Build();
var factory = app.Services.GetRequiredService<ITurboHttpClientFactory>();
var client = factory.CreateClient("api");

var response = await client.SendAsync(
    new HttpRequestMessage(HttpMethod.Get, "/users"),
    CancellationToken.None);
```

### Server

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);
});

var app = builder.Build();

app.MapTurboGet("/health", () => new { status = "healthy" });
app.MapTurboGet("/users/{id}", (int id) => new { id, name = "User " + id });

await app.RunAsync();
```

::: tip About AddTurboKestrel
Despite the name, TurboHTTP Server is a fully standalone HTTP server built on Akka.Streams with its own TCP/QUIC transport layer. The method is named `AddTurboKestrel` for configuration familiarity — it does not use or depend on Kestrel.
:::

## Next Steps

- [Client Quick Start](./client) — build your first TurboHTTP client
- [Server Quick Start](./server) — build your first TurboHTTP server
- [Architecture Overview](./architecture) — understand how the pipeline works
- [Migration from HttpClient](./migration) — coming from `System.Net.Http`?
```

- [ ] **Step 3: Create client.md quick start**

Write to `docs/getting-started/client.md`:

```markdown
# Client Quick Start

Build a working TurboHTTP client in under 5 minutes.

## 1. Install

```bash
dotnet add package TurboHTTP
```

## 2. Register a Client

```csharp
using TurboHTTP;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboHttpClient("api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
});

var app = builder.Build();
```

## 3. Send a Request

```csharp
var factory = app.Services.GetRequiredService<ITurboHttpClientFactory>();
var client = factory.CreateClient("api");

var response = await client.SendAsync(
    new HttpRequestMessage(HttpMethod.Get, "/users"),
    CancellationToken.None);

response.EnsureSuccessStatusCode();
Console.WriteLine(await response.Content.ReadAsStringAsync());
```

## 4. Add Features

Features are opt-in via the fluent builder:

```csharp
builder.Services.AddTurboHttpClient("api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRetry()              // automatic retries for GET, PUT, DELETE
.WithCache()              // in-memory HTTP caching with ETag
.WithCookies()            // automatic cookie storage and injection
.WithRedirect()           // follow redirect chains
.WithDecompression();     // gzip/deflate/Brotli decompression
```

Each `.With*()` method adds a pipeline stage. They compose — order doesn't matter.

## 5. High-Throughput Usage

For batch processing, use the channel-based API instead of `SendAsync`:

```csharp
var client = factory.CreateClient("api");

// Producer: write requests without waiting
await client.Requests.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/item/1"));
await client.Requests.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/item/2"));
client.Requests.Complete();

// Consumer: read responses as they arrive
await foreach (var response in client.Responses.ReadAllAsync())
{
    Console.WriteLine($"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
}
```

With HTTP/2, all requests flow over a single TCP connection as multiplexed streams. The channel applies backpressure if the connection can't keep up.

## Next Steps

- [Installation & Setup](/client/installation) — DI registration, named clients, typed clients
- [Configuration](/client/configuration) — all client options
- [Full Client Guide](/client/) — retries, caching, cookies, HTTP/2, HTTP/3
- [Real-World Scenarios](/client/scenarios) — combined feature examples
```

- [ ] **Step 4: Create server.md quick start**

Write to `docs/getting-started/server.md`:

```markdown
# Server Quick Start

Build a working TurboHTTP server in under 5 minutes.

::: tip Standalone Server
TurboHTTP Server is a fully standalone HTTP server built on Akka.Streams with its own TCP/QUIC transport (Servus.Akka.Transport). The `AddTurboKestrel` method name is a configuration convention — it does not use Kestrel.
:::

## 1. Install

```bash
dotnet add package TurboHTTP
```

## 2. Configure the Server

```csharp
using TurboHTTP.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);
});

var app = builder.Build();
```

## 3. Add Routes

```csharp
app.MapTurboGet("/health", () => new { status = "healthy" });

app.MapTurboGet("/users/{id}", (int id) => new { id, name = "User " + id });

app.MapTurboPost("/users", (CreateUserRequest req) =>
    new { created = true, name = req.Name });

app.MapTurboDelete("/users/{id}", (int id) => new { deleted = true, id });

await app.RunAsync();

public sealed record CreateUserRequest(string Name, string Email);
```

## 4. Add Middleware

```csharp
app.UseTurbo(async (context, next) =>
{
    Console.WriteLine($"[{context.Request.Method}] {context.Request.Path}");
    await next(context);
});
```

## 5. Route Groups

```csharp
var api = app.MapTurboGroup("/api/v1");
api.MapGet("/users", () => new[] { "Alice", "Bob" });
api.MapPost("/users", (CreateUserRequest req) => new { created = true });
```

## 6. Test It

```bash
curl http://localhost:5100/health
curl http://localhost:5100/users/42
curl -X POST http://localhost:5100/users -H "Content-Type: application/json" -d '{"name":"Alice","email":"alice@example.com"}'
```

## Server Architecture

TurboHTTP Server uses an actor hierarchy for connection management:

```
ServerSupervisorActor
├── ListenerActor (endpoint :5100)
│   ├── ConnectionActor (client A)
│   └── ConnectionActor (client B)
└── ListenerActor (endpoint :5101)
    └── ConnectionActor (client C)
```

Each connection gets its own actor, and protocol engines (HTTP/1.0, 1.1, 2, 3) are selected via ALPN negotiation.

## Next Steps

- [Installation & Setup](/server/installation) — endpoints, HTTPS, protocols
- [Middleware Pipeline](/server/middleware) — composition, error handling
- [Routing](/server/routing) — parameters, binding, groups
- [Entity Gateway](/server/entity-gateway) — actor-based stateful handling
- [Real-World Scenarios](/server/scenarios) — combined feature examples
```

- [ ] **Step 5: Create architecture.md overview**

Write to `docs/getting-started/architecture.md`. Adapt content from `docs/architecture/index.md` — keep the LikeC4 diagram, the pipeline description, and key characteristics. Add a section explaining the server architecture (actor hierarchy, standalone transport). Link to deep-dive architecture pages at the bottom.

Key sections:
1. The Request Pipeline (client side) — reuse the pipeline ASCII diagram from `architecture/index.md`
2. Server Architecture — explain `ServerSupervisorActor` → `ListenerActor` → `ConnectionActor`, TCP/QUIC transport via Servus.Akka.Transport, protocol engine selection via ALPN
3. Key Characteristics (automatic, efficient, composable)
4. Links to deep-dive pages: [Pipeline](/architecture/pipeline), [Engines](/architecture/engines), [Handlers](/architecture/handlers), [Scenarios](/architecture/scenarios), [Extending](/architecture/extending)

Include the LikeC4 diagram:
```markdown
<ClientOnly>
  <LikeC4Diagram viewId="index" :height="300" />
</ClientOnly>
```

- [ ] **Step 6: Move migration.md to getting-started**

```bash
git mv docs/client/migration.md docs/getting-started/migration.md
```

After moving, update internal links in the file:
- Change relative links like `./configuration` to `/client/configuration`
- Change relative links like `./installation` to `/client/installation`

- [ ] **Step 7: Verify dev server starts**

Run from `docs/`:
```bash
npx vitepress dev
```
Expected: Dev server starts. Navigate to `http://localhost:5173/getting-started/` — page renders. Some links may 404 (pages not yet created) — that's expected.

- [ ] **Step 8: Commit**

```bash
git add docs/getting-started/
git commit -m "docs: create Getting Started section with quick starts and architecture overview"
```

---

### Task 3: Register theme components

**Files:**
- Modify: `docs/.vitepress/theme/index.ts`

- [ ] **Step 1: Update theme to register HomePage and CodeTabs components**

```typescript
import type { Theme } from 'vitepress'
import DefaultTheme from 'vitepress/theme'
import LikeC4Diagram from '../components/LikeC4Diagram.vue'
import HomePage from '../components/HomePage.vue'
import CodeTabs from '../components/CodeTabs.vue'
import './custom.css'

export default {
    extends: DefaultTheme,
    enhanceApp({ app })
    {
        app.component('LikeC4Diagram', LikeC4Diagram)
        app.component('HomePage', HomePage)
        app.component('CodeTabs', CodeTabs)
    },
} satisfies Theme
```

Note: The actual Vue components are created in Phase 5. VitePress will warn about missing components until then — that's fine.

- [ ] **Step 2: Commit**

```bash
git add docs/.vitepress/theme/index.ts
git commit -m "docs: register HomePage and CodeTabs theme components"
```

---

## Phase 2: API Reference Split + Accuracy Fixes

### Task 4: Create api/client.md (client interface reference)

**Files:**
- Create: `docs/api/client.md`

- [ ] **Step 1: Write the client API reference page**

Write to `docs/api/client.md`. This covers `ITurboHttpClientFactory` and `ITurboHttpClient`. Use the verified API surface from the plan header.

Key sections:

```markdown
# Client API

## ITurboHttpClientFactory

\```csharp
public interface ITurboHttpClientFactory
{
    ITurboHttpClient CreateClient(string name);
}
\```

Registered via dependency injection. ...

## ITurboHttpClient

\```csharp
public interface ITurboHttpClient : IDisposable
{
    Uri? BaseAddress { get; set; }
    HttpRequestHeaders DefaultRequestHeaders { get; }
    Version DefaultRequestVersion { get; set; }
    HttpVersionPolicy DefaultVersionPolicy { get; set; }
    TimeSpan Timeout { get; set; }
    ChannelWriter<HttpRequestMessage> Requests { get; }
    ChannelReader<HttpResponseMessage> Responses { get; }

    void CancelPendingRequests();
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}
\```
```

Include code examples for each property/method. Adapt from the existing `api/index.md` lines 29–148 but:
- **Remove** `MaxResponseContentBufferSize` (does not exist on the interface)
- Keep BaseAddress, DefaultRequestHeaders, DefaultRequestVersion, DefaultVersionPolicy, Timeout, Requests/Responses, CancelPendingRequests, SendAsync

- [ ] **Step 2: Commit**

```bash
git add docs/api/client.md
git commit -m "docs: create api/client.md with verified ITurboHttpClient reference"
```

---

### Task 5: Create api/client-options.md (verified option defaults)

**Files:**
- Create: `docs/api/client-options.md`

- [ ] **Step 1: Write the client options reference with VERIFIED defaults**

Write to `docs/api/client-options.md`. This covers `TurboClientOptions`, `Http1Options`, `Http2Options`, `Http3Options`.

Use the verified API surface from the plan header. Critical corrections vs current docs:

| Property | Old (WRONG) | New (CORRECT) |
|----------|-------------|---------------|
| `Http2.MaxFrameSize` | 16384 (16 KiB) | `64 * 1024` (64 KiB) |
| `Http2.HeaderTableSize` | 4096 | `64 * 1024` (64 KiB) |
| `Http2.InitialStreamWindowSize` | 65535 | `2 * 1024 * 1024` (2 MiB) |
| `Http3.QpackMaxTableCapacity` | 4096 | `16 * 1024` (16 KiB) |

Properties to ADD (were undocumented):
- `TurboClientOptions.MaxBufferedBodySize` — default `4 * 1024 * 1024` (4 MiB)
- `TurboClientOptions.MaxStreamedBodySize` — default `null` (unlimited)
- `TurboClientOptions.SocketSendBufferSize` — default `null`
- `TurboClientOptions.SocketReceiveBufferSize` — default `null`
- `Http1Options.AutoHost` — default `true`
- `Http1Options.AutoAcceptEncoding` — default `true`
- `Http3Options.MaxReconnectBufferSize` — default `64`
- `Http3Options.MaxConcurrentStreams` — default `100`

Properties to REMOVE (don't exist):
- `Http1Options.MaxBatchWeight`
- `Http2Options.MaxBatchWeight`
- `Http3Options.AllowEarlyData`
- `Http3Options.AllowServerPush`
- `Http3Options.MaxBatchWeight`

Structure: Full `TurboClientOptions` class with signature, then a table for each options group (Connection, Http1, Http2, Http3, TLS, Proxy, Auth).

Follow CLAUDE.md size literal rules: always `N * 1024` or `N * 1024 * 1024`, never raw numbers.

- [ ] **Step 2: Commit**

```bash
git add docs/api/client-options.md
git commit -m "docs: create api/client-options.md with verified defaults from source"
```

---

### Task 6: Create api/feature-options.md

**Files:**
- Create: `docs/api/feature-options.md`

- [ ] **Step 1: Write the feature options reference**

Write to `docs/api/feature-options.md`. Covers `RetryOptions`, `CacheOptions`, `RedirectOptions`, `CompressionOptions`, `Expect100Options`, and all builder extension methods.

Content sources:
- Adapt from existing `api/index.md` lines 282–397 (Feature Options section)
- These are mostly correct — verify each class signature against the plan header

Include the builder extensions section:

```markdown
## Builder Extensions

All features are composed via the fluent builder:

\```csharp
public static class TurboHttpClientBuilderExtensions
{
    ITurboHttpClientBuilder WithCookies(this ITurboHttpClientBuilder builder);
    ITurboHttpClientBuilder WithCookies(this ITurboHttpClientBuilder builder, ICookieStore store);
    ITurboHttpClientBuilder WithCache(this ITurboHttpClientBuilder builder, Action<CacheOptions>? configure = null);
    ITurboHttpClientBuilder WithCache(this ITurboHttpClientBuilder builder, ICacheStore store, Action<CacheOptions>? configure = null);
    ITurboHttpClientBuilder WithRetry(this ITurboHttpClientBuilder builder, Action<RetryOptions>? configure = null);
    ITurboHttpClientBuilder WithRedirect(this ITurboHttpClientBuilder builder, Action<RedirectOptions>? configure = null);
    ITurboHttpClientBuilder WithDecompression(this ITurboHttpClientBuilder builder, bool enabled = true);
    ITurboHttpClientBuilder WithRequestCompression(this ITurboHttpClientBuilder builder, Action<CompressionOptions>? configure = null);
    ITurboHttpClientBuilder WithExpectContinue(this ITurboHttpClientBuilder builder, Action<Expect100Options>? configure = null);
    ITurboHttpClientBuilder AddHandler<T>(this ITurboHttpClientBuilder builder) where T : TurboHandler;
    ITurboHttpClientBuilder UseRequest(this ITurboHttpClientBuilder builder, Func<HttpRequestMessage, HttpRequestMessage> transform);
    ITurboHttpClientBuilder UseResponse(this ITurboHttpClientBuilder builder, Func<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> transform);
}
\```
```

Also document `TurboHandler` base class:

```markdown
## TurboHandler

\```csharp
public abstract class TurboHandler
{
    public virtual HttpRequestMessage ProcessRequest(HttpRequestMessage request) => request;
    public virtual HttpResponseMessage ProcessResponse(HttpRequestMessage original, HttpResponseMessage response) => response;
}
\```
```

- [ ] **Step 2: Commit**

```bash
git add docs/api/feature-options.md
git commit -m "docs: create api/feature-options.md with builder extensions"
```

---

### Task 7: Create api/server.md (server API reference)

**Files:**
- Create: `docs/api/server.md`

- [ ] **Step 1: Write the server API reference**

Write to `docs/api/server.md`. Covers `TurboServerOptions` (with nested Http1/2/3ServerOptions), `TurboServerServiceCollectionExtensions`, `TurboRoutingExtensions`, `TurboRouteHandlerBuilder`, `TurboRouteGroupBuilder`, `ITurboMiddleware`.

Critical narrative fix: Frame this as TurboHTTP's own standalone server, NOT Kestrel.

```markdown
# Server API

TurboHTTP Server is a standalone HTTP server built on Akka.Streams. Despite the `AddTurboKestrel` method name (kept for configuration familiarity), it uses its own transport layer via Servus.Akka.Transport.

## Registration

\```csharp
public static class TurboServerServiceCollectionExtensions
{
    IServiceCollection AddTurboKestrel(this IServiceCollection services, Action<TurboServerOptions>? configure = null);
    IServiceCollection AddTurboKestrel(this IServiceCollection services, IConfiguration configuration, Action<TurboServerOptions>? configure = null);
}
\```
```

Include `TurboServerOptions` with verified properties from source, `TurboRoutingExtensions` signatures, `TurboRouteHandlerBuilder` methods, `TurboRouteGroupBuilder` methods, and `ITurboMiddleware` interface.

Adapt from existing `api/index.md` lines 401–460, reframing all Kestrel references.

- [ ] **Step 2: Commit**

```bash
git add docs/api/server.md
git commit -m "docs: create api/server.md with standalone server framing"
```

---

### Task 8: Create api/entity-gateway.md (verified entity builder API)

**Files:**
- Create: `docs/api/entity-gateway.md`

- [ ] **Step 1: Write the entity gateway API reference**

Write to `docs/api/entity-gateway.md`. Covers `TurboEntityBuilder`, `TurboEntityMethodBuilder`, `IEntityActorResolver`, and the `MapTurboEntity` overloads.

Critical fixes:
- **Remove** `WithEntityKey(string paramName)` — does not exist
- **Document both** `MapTurboEntity` overloads:
  ```csharp
  MapTurboEntity(string pattern, Action<TurboEntityBuilder> configure)
  MapTurboEntity<TActorKey>(string pattern, Action<TurboEntityBuilder> configure)
  ```
- **Add** `UseActorRef` overloads (3 variants):
  ```csharp
  UseActorRef<TActorKey>()
  UseActorRef(Func<IServiceProvider, IActorRef> factory)
  UseActorRef(Func<IReadOnlyActorRegistry, IActorRef> actorRefFactory)
  ```
- **Add** `TurboEntityMethodBuilder` methods: `AcceptedResponse()`, `WithTimeout(TimeSpan)`

- [ ] **Step 2: Commit**

```bash
git add docs/api/entity-gateway.md
git commit -m "docs: create api/entity-gateway.md with verified builder API"
```

---

### Task 9: Rewrite api/index.md as overview page

**Files:**
- Modify: `docs/api/index.md`

- [ ] **Step 1: Replace the monolith with a concise overview linking to sub-pages**

Replace the entire content of `docs/api/index.md` with:

```markdown
# API Reference

TurboHTTP's public API is organized into client, server, and feature configuration.

## Client

| Type | Description | Reference |
|------|-------------|-----------|
| `ITurboHttpClientFactory` | Creates named client instances | [Client API](./client) |
| `ITurboHttpClient` | The HTTP client — `SendAsync` and channel-based API | [Client API](./client) |
| `TurboClientOptions` | Connection, TLS, proxy, and protocol settings | [Client Options](./client-options) |
| `Http1Options` / `Http2Options` / `Http3Options` | Per-protocol tuning | [Client Options](./client-options) |
| `RetryOptions` / `CacheOptions` / `RedirectOptions` | Feature configuration | [Feature Options](./feature-options) |
| Builder extensions (`.WithRetry()`, `.WithCache()`, etc.) | Fluent feature composition | [Feature Options](./feature-options) |

## Server

| Type | Description | Reference |
|------|-------------|-----------|
| `AddTurboKestrel()` | Server registration (standalone, not Kestrel) | [Server API](./server) |
| `TurboServerOptions` | Endpoints, protocols, timeouts | [Server API](./server) |
| `MapTurboGet/Post/Put/Delete/Patch()` | Route registration | [Server API](./server) |
| `UseTurbo()` / `ITurboMiddleware` | Middleware pipeline | [Server API](./server) |
| `TurboEntityBuilder` | Actor-based entity routing | [Entity Gateway API](./entity-gateway) |

## DI Registration

### Client

```csharp
// Named client
builder.Services.AddTurboHttpClient("api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRetry()
.WithCache();

// Default (unnamed) client
builder.Services.AddTurboHttpClient(options => { ... });

// Typed client
builder.Services.AddTurboHttpClient<IMyApiClient>(options => { ... });
```

### Server

```csharp
builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);
});
```
```

- [ ] **Step 2: Commit**

```bash
git add docs/api/index.md
git commit -m "docs: rewrite api/index.md as overview linking to sub-pages"
```

---

## Phase 3: Server Narrative Correction

### Task 10: Rewrite server/index.md without Kestrel framing

**Files:**
- Modify: `docs/server/index.md`

- [ ] **Step 1: Fix the Kestrel narrative throughout server/index.md**

Apply these changes to `docs/server/index.md`:

1. **Line 1-3**: Change intro paragraph from "integrates with ASP.NET Core via Kestrel" to:
   > TurboHTTP Server is a high-performance, standalone HTTP server for .NET built on Akka.Streams. It provides middleware, routing, entity gateway, parameter binding, and actor-based connection lifecycle management — all with zero buffer copies and minimal allocations.

2. **Line 6**: Change tip from "See Installation & Setup for NuGet packages and Kestrel configuration" to:
   > See [Installation & Setup](./installation) for NuGet packages and endpoint configuration.

3. **Line 25**: Change comment "Register TurboHTTP Server with Kestrel" to:
   > // Register TurboHTTP Server

4. **Feature table row** (line ~290): Change "Kestrel Integration" to:
   > **Standalone Server** | Actor-based HTTP server with TCP/QUIC transport via Servus.Akka.Transport

5. **Next Steps section**: Change "NuGet packages, Kestrel configuration" to "NuGet packages, endpoint configuration"

6. **Remove** the `entity.WithEntityKey("id")` call from the Entity Gateway example (~line 176) — this method does not exist.

7. **Fix route group method names** (~lines 54-57): Inside `MapTurboGroup`, methods are `MapGet`, `MapPost` etc. (no "Turbo" prefix — that prefix is only on the `WebApplication` extensions). Change `api.MapTurboGet(...)` / `api.MapTurboPost(...)` to `api.MapGet(...)` / `api.MapPost(...)`.

7. **Configuration section** (~line 224): Change comment "Register TurboHTTP Server with Kestrel" to just the setup code without Kestrel framing.

8. **appsettings.json section** (~line 253): Change the `"Kestrel"` key to `"TurboKestrel"` (or explain the naming convention with a callout).

- [ ] **Step 2: Commit**

```bash
git add docs/server/index.md
git commit -m "docs: reframe server/index.md as standalone server, remove Kestrel narrative"
```

---

### Task 11: Update server/installation.md without Kestrel framing

**Files:**
- Modify: `docs/server/installation.md`

- [ ] **Step 1: Fix Kestrel references in installation.md**

Read `docs/server/installation.md` fully first. Apply changes:

1. Add a callout near the top (after the install section):
   ```markdown
   ::: tip About AddTurboKestrel
   TurboHTTP Server is a fully standalone HTTP server — it does not use or depend on Kestrel. The `AddTurboKestrel` method name follows ASP.NET Core configuration conventions for familiarity. Under the hood, TurboHTTP uses its own TCP/QUIC transport layer (Servus.Akka.Transport).
   :::
   ```

2. Replace any "Configure Kestrel" headings with "Configure Endpoints"

3. Replace "Kestrel endpoint" references with "server endpoint"

- [ ] **Step 2: Commit**

```bash
git add docs/server/installation.md
git commit -m "docs: reframe server/installation.md as standalone server"
```

---

### Task 12: Update server/configuration.md

**Files:**
- Modify: `docs/server/configuration.md`

- [ ] **Step 1: Fix Kestrel references in configuration.md**

Read `docs/server/configuration.md` fully. Apply changes:

1. Reframe any "Kestrel options" as "TurboHTTP Server options"
2. Add the AddTurboKestrel naming callout if not already present
3. Verify all option defaults match the source (from verified API in plan header):
   - `KeepAliveTimeout`: 120s
   - `RequestHeadersTimeout`: 30s
   - `GracefulShutdownTimeout`: 30s
   - `Http2ServerOptions.MaxConcurrentStreams`: 100
   - `Http2ServerOptions.InitialWindowSize`: 65535
   - etc.

- [ ] **Step 2: Commit**

```bash
git add docs/server/configuration.md
git commit -m "docs: reframe server/configuration.md, verify option defaults"
```

---

### Task 13: Update server/hosting.md with actor hierarchy

**Files:**
- Modify: `docs/server/hosting.md`

- [ ] **Step 1: Verify and enhance hosting.md**

Read `docs/server/hosting.md` fully. The file already describes the actor hierarchy correctly (ServerSupervisorActor → ListenerActor → ConnectionActor). Make these changes:

1. Replace any "Kestrel" references with "TurboHTTP Server"
2. Add a brief section explaining the transport layer:
   ```markdown
   ## Transport Layer

   TurboHTTP uses Servus.Akka.Transport for network I/O:

   - **TCP**: `TcpListenerFactory` handles HTTP/1.0, HTTP/1.1, and HTTP/2 connections
   - **QUIC**: `QuicListenerFactory` handles HTTP/3 connections

   Protocol engines (`Http10ServerEngine`, `Http11ServerEngine`, `Http20ServerEngine`, `Http30ServerEngine`) are selected via ALPN negotiation when TLS is enabled, or default to HTTP/1.1 for plaintext connections.
   ```

3. Verify the "How the Server Starts" section accurately reflects the code (it should — it already mentions ActorSystem, Materializer, ServerSupervisorActor, ListenerActors)

- [ ] **Step 2: Commit**

```bash
git add docs/server/hosting.md
git commit -m "docs: add transport layer section to server/hosting.md"
```

---

## Phase 4: Content Restructuring

### Task 14: Rewrite client/index.md as overview

**Files:**
- Modify: `docs/client/index.md`

- [ ] **Step 1: Remove duplicate quickstart, make it an overview page**

Replace the content of `docs/client/index.md`. Remove the "Quick Start" code block (now in `getting-started/client.md`) and make this a feature overview page.

Keep:
- The intro paragraph
- The "What's Included" feature table
- The "High-Throughput Usage" section (channel API — unique to this page)
- The "Next Steps" links

Remove:
- The "Quick Start" section with the code block (lines 13-40)
- The `dotnet add package` install command (covered in getting-started)

Add at the top:
```markdown
::: tip New to TurboHTTP?
Start with the [Client Quick Start](/getting-started/client) for a step-by-step setup guide.
:::
```

Update the Migration link to point to new location:
```markdown
- [Migration from HttpClient](/getting-started/migration)
```

- [ ] **Step 2: Commit**

```bash
git add docs/client/index.md
git commit -m "docs: rewrite client/index.md as overview, remove duplicate quickstart"
```

---

### Task 15: Delete old quickstart and why directories

**Files:**
- Delete: `docs/quickstart/index.md`
- Delete: `docs/why/index.md`

- [ ] **Step 1: Remove the old directories**

```bash
git rm docs/quickstart/index.md
git rm docs/why/index.md
rmdir docs/quickstart docs/why
```

- [ ] **Step 2: Commit**

```bash
git commit -m "docs: remove quickstart/ and why/ (content absorbed into getting-started and homepage)"
```

---

### Task 16: Add cross-links to client feature pages

**Files:**
- Modify: `docs/client/caching.md`
- Modify: `docs/client/retries.md`
- Modify: `docs/client/cookies.md`
- Modify: `docs/client/redirects.md`
- Modify: `docs/client/connection-pooling.md`
- Modify: `docs/client/content-encoding.md`
- Modify: `docs/client/http2.md`
- Modify: `docs/client/http3.md`

- [ ] **Step 1: Add architecture cross-link callout to each client feature page**

Append to the bottom of each page (before any existing "Next Steps" or at the very end):

```markdown
::: info How it works
See [Architecture: Request Pipeline](/architecture/pipeline) to understand how this feature fits into the processing pipeline.
:::
```

For `http2.md` and `http3.md`, use a more specific link:
```markdown
::: info How it works
See [Architecture: Protocol Engines](/architecture/engines) for details on how protocol negotiation and engine selection work.
:::
```

- [ ] **Step 2: Commit**

```bash
git add docs/client/caching.md docs/client/retries.md docs/client/cookies.md docs/client/redirects.md docs/client/connection-pooling.md docs/client/content-encoding.md docs/client/http2.md docs/client/http3.md
git commit -m "docs: add architecture cross-links to client feature pages"
```

---

### Task 17: Add cross-links to architecture pages

**Files:**
- Modify: `docs/architecture/pipeline.md`
- Modify: `docs/architecture/engines.md`
- Modify: `docs/architecture/handlers.md`

- [ ] **Step 1: Add feature guide cross-links to architecture pages**

At the bottom of `docs/architecture/pipeline.md`, add:
```markdown
## Related Guides

- [Automatic Retries](/client/retries) — configure retry behavior
- [HTTP Caching](/client/caching) — configure caching
- [Cookie Management](/client/cookies) — configure cookie handling
- [Redirects](/client/redirects) — configure redirect following
- [Connection Pooling](/client/connection-pooling) — pool tuning
```

At the bottom of `docs/architecture/engines.md`, add:
```markdown
## Related Guides

- [HTTP/2 & Multiplexing](/client/http2) — HTTP/2 configuration
- [HTTP/3 & QUIC](/client/http3) — HTTP/3 configuration
- [Server Configuration](/server/configuration) — server protocol settings
```

At the bottom of `docs/architecture/handlers.md`, add:
```markdown
## Related Guides

- [Middleware Pipeline](/server/middleware) — server middleware composition
- [Feature Options](/api/feature-options) — client pipeline extensions
```

- [ ] **Step 2: Commit**

```bash
git add docs/architecture/pipeline.md docs/architecture/engines.md docs/architecture/handlers.md
git commit -m "docs: add feature guide cross-links to architecture pages"
```

---

## Phase 5: New Content

### Task 18: Write client/scenarios.md

**Files:**
- Create: `docs/client/scenarios.md`

- [ ] **Step 1: Write real-world client scenario examples**

Write to `docs/client/scenarios.md`. Four scenarios, each with full DI registration + usage code. Use ONLY verified API from plan header.

```markdown
# Real-World Scenarios

Practical examples combining multiple TurboHTTP features for common use cases.

## Authenticated REST API Client

A typical backend service calling a REST API with authentication, automatic retries, and caching.

\```csharp
builder.Services.AddTurboHttpClient("billing-api", options =>
{
    options.BaseAddress = new Uri("https://billing.internal.example.com/v2/");
    options.Http2.MaxConcurrentStreams = 50;
})
.WithRetry(r => { r.MaxRetries = 5; r.RespectRetryAfter = true; })
.WithCache(c => { c.MaxEntries = 500; })
.WithRedirect()
.UseRequest(req =>
{
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetToken());
    return req;
});
\```

Usage:

\```csharp
var client = factory.CreateClient("billing-api");

var response = await client.SendAsync(
    new HttpRequestMessage(HttpMethod.Get, "invoices?status=unpaid"),
    cancellationToken);
// Retry handles transient 503s; cache returns 304 Not Modified on repeat calls
\```

**Features in play:** retry (5 attempts, respects Retry-After), cache (500 entries, ETag conditional requests), redirect following, custom request transform for auth header.

## Web Scraper with Session Cookies

Scraping a website that requires login and session management.

\```csharp
builder.Services.AddTurboHttpClient("scraper", options =>
{
    options.BaseAddress = new Uri("https://shop.example.com/");
})
.WithCookies()
.WithRedirect(r => { r.MaxRedirects = 5; })
.WithDecompression();
\```

Usage:

\```csharp
var client = factory.CreateClient("scraper");

// Login — cookies are stored automatically from Set-Cookie headers
await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "login")
{
    Content = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["username"] = "user@example.com",
        ["password"] = "secret",
    })
}, ct);

// Subsequent requests automatically include session cookies
var catalog = await client.SendAsync(
    new HttpRequestMessage(HttpMethod.Get, "catalog?page=1"), ct);
// Redirects followed (login may redirect), response decompressed (gzip/brotli)
\```

**Features in play:** cookies (auto-stored on login, injected on every subsequent request), redirects (follows post-login redirect), decompression (handles gzip catalog pages).

## High-Throughput Batch Processor

Processing thousands of API calls using the channel API with HTTP/2 multiplexing.

\```csharp
builder.Services.AddTurboHttpClient("batch", options =>
{
    options.BaseAddress = new Uri("https://api.example.com/");
    options.Http2.MaxConcurrentStreams = 100;
    options.Http2.MaxConnectionsPerServer = 2;
})
.WithRetry();
\```

Usage:

\```csharp
var client = factory.CreateClient("batch");
client.DefaultRequestVersion = HttpVersion.Version20;

var ids = Enumerable.Range(1, 10_000);

// Producer: write requests without waiting
var producer = Task.Run(async () =>
{
    foreach (var id in ids)
    {
        await client.Requests.WriteAsync(
            new HttpRequestMessage(HttpMethod.Get, $"items/{id}"), ct);
    }
    client.Requests.Complete();
});

// Consumer: process responses as they arrive
var results = new ConcurrentBag<ItemResult>();
var consumer = Task.Run(async () =>
{
    await foreach (var response in client.Responses.ReadAllAsync(ct))
    {
        var item = await response.Content.ReadFromJsonAsync<ItemResult>(ct);
        results.Add(item);
    }
});

await Task.WhenAll(producer, consumer);
Console.WriteLine($"Processed {results.Count} items");
\```

**Features in play:** HTTP/2 multiplexing (100 concurrent streams over 2 connections), channel API (backpressure-aware producer/consumer), retry (transient failures retried automatically).

## Microservice Communication

Service-to-service calls with timeouts and HTTP/2.

\```csharp
builder.Services.AddTurboHttpClient("orders-service", options =>
{
    options.BaseAddress = new Uri("https://orders.internal:8443/");
    options.ConnectTimeout = TimeSpan.FromSeconds(5);
})
.WithRetry(r => { r.MaxRetries = 2; })
.WithDecompression();
\```

Usage:

\```csharp
var client = factory.CreateClient("orders-service");
client.DefaultRequestVersion = HttpVersion.Version20;
client.Timeout = TimeSpan.FromSeconds(10);

var response = await client.SendAsync(
    new HttpRequestMessage(HttpMethod.Get, "orders/latest"), ct);

if (response.IsSuccessStatusCode)
{
    var orders = await response.Content.ReadFromJsonAsync<Order[]>(ct);
}
\```

**Features in play:** HTTP/2 (single connection, multiplexed), retry (2 attempts for transient failures), connect timeout (fast failure on unreachable service), request timeout (10s per-request).
```

- [ ] **Step 2: Commit**

```bash
git add docs/client/scenarios.md
git commit -m "docs: add real-world client scenario examples"
```

---

### Task 19: Write server/scenarios.md

**Files:**
- Create: `docs/server/scenarios.md`

- [ ] **Step 1: Write real-world server scenario examples**

Write to `docs/server/scenarios.md`. Four scenarios with full `Program.cs` and curl test commands.

```markdown
# Real-World Scenarios

Practical examples combining TurboHTTP Server features for common patterns.

## REST API with Validation and Entity Gateway

A CRUD API backed by Akka.NET actors for stateful order management.

\```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);
});

var app = builder.Build();

// Validation middleware
app.UseTurbo(async (context, next) =>
{
    if (context.Request.ContentType?.Contains("application/json") == false
        && context.Request.Method is "POST" or "PUT" or "PATCH")
    {
        context.Response.StatusCode = 415;
        await context.Response.WriteAsJsonAsync(new { error = "JSON required" });
        return;
    }
    await next(context);
});

// Standard routes
app.MapTurboGet("/health", () => new { status = "healthy" });

// Entity gateway for orders
app.MapTurboEntity("/orders/{id}", entity =>
{
    entity.UseResolver<OrderResolver>();
    entity.OnGet((int id) => new GetOrder(id));
    entity.OnPost((int id, CreateOrderRequest req) => new CreateOrder(id, req.Items));
    entity.OnPut((int id, UpdateOrderRequest req) => new UpdateOrder(id, req.Status));
    entity.OnDelete((int id) => new CancelOrder(id));
    entity.MapResponse<OrderResponse>(async (ctx, resp) =>
    {
        ctx.Response.StatusCode = 200;
        await ctx.Response.WriteAsJsonAsync(resp);
    });
    entity.WithTimeout(TimeSpan.FromSeconds(5));
});

await app.RunAsync();
\```

Test:
\```bash
curl http://localhost:5100/health
curl -X POST http://localhost:5100/orders/1 -H "Content-Type: application/json" -d '{"items":["widget"]}'
curl http://localhost:5100/orders/1
curl -X DELETE http://localhost:5100/orders/1
\```

## Middleware Pipeline: Logging + Auth + CORS

Composing middleware for a production API.

\```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);
    options.ListenLocalhost(5101, listen => listen.UseHttps());
});

var app = builder.Build();

// 1. Request logging
app.UseTurbo(async (context, next) =>
{
    var start = Stopwatch.GetTimestamp();
    Console.WriteLine($"--> {context.Request.Method} {context.Request.Path}");
    await next(context);
    var elapsed = Stopwatch.GetElapsedTime(start);
    Console.WriteLine($"<-- {context.Response.StatusCode} ({elapsed.TotalMilliseconds:F1}ms)");
});

// 2. CORS headers
app.UseTurbo(async (context, next) =>
{
    context.Response.Headers["Access-Control-Allow-Origin"] = "*";
    context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE";
    context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";

    if (context.Request.Method == "OPTIONS")
    {
        context.Response.StatusCode = 204;
        return;
    }
    await next(context);
});

// 3. Auth middleware on /api prefix
app.MapTurbo("/api", apiBuilder =>
{
    apiBuilder.UseTurbo(async (context, next) =>
    {
        if (!context.Request.Headers.ContainsKey("Authorization"))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
            return;
        }
        await next(context);
    });

    apiBuilder.MapTurboGet("/users", () => new[] { "Alice", "Bob" });
    apiBuilder.MapTurboGet("/users/{id}", (int id) => new { id, name = "User " + id });
});

// Public routes (no auth)
app.MapTurboGet("/health", () => new { status = "healthy" });

await app.RunAsync();
\```

**Middleware execution order:** Logging → CORS → Auth (on /api only) → Route handler → Auth after → CORS after → Logging after (with timing).

## Actor-Based CQRS

Separate read and write paths using entity gateway message factories.

\```csharp
// Commands (write path)
public sealed record CreateAccount(string Id, string Owner);
public sealed record DepositFunds(string Id, decimal Amount);
public sealed record WithdrawFunds(string Id, decimal Amount);

// Queries (read path)
public sealed record GetAccountBalance(string Id);
public sealed record GetAccountHistory(string Id);

// Responses
public sealed record AccountBalance(string Id, decimal Balance);
public sealed record AccountHistory(string Id, IReadOnlyList<string> Transactions);

// Route setup
app.MapTurboEntity("/accounts/{id}", entity =>
{
    entity.UseActorRef<AccountActorKey>();

    // Queries (GET)
    entity.OnGet((string id) => new GetAccountBalance(id));

    // Commands (POST/PUT/DELETE)
    entity.OnPost((string id, CreateAccountRequest req) => new CreateAccount(id, req.Owner));
    entity.OnPut((string id, DepositRequest req) => new DepositFunds(id, req.Amount));
    entity.OnDelete((string id) => new WithdrawFunds(id, 0));

    entity.MapResponse<AccountBalance>(async (ctx, resp) =>
    {
        await ctx.Response.WriteAsJsonAsync(resp);
    });
    entity.MapResponse<AccountHistory>(async (ctx, resp) =>
    {
        await ctx.Response.WriteAsJsonAsync(resp);
    });
});
\```

## Multi-Protocol Endpoint

Serving HTTP/1.1 and HTTP/2 on the same server.

\```csharp
builder.Services.AddTurboKestrel(options =>
{
    // Plaintext HTTP/1.1 for health checks and internal traffic
    options.ListenLocalhost(5100);

    // HTTPS with HTTP/1.1 + HTTP/2 for client-facing traffic
    options.ListenLocalhost(5101, listen =>
    {
        listen.UseHttps();
        listen.Protocols = HttpProtocols.Http1AndHttp2;
    });

    // Server-wide tuning
    options.KeepAliveTimeout = TimeSpan.FromSeconds(60);
    options.GracefulShutdownTimeout = TimeSpan.FromSeconds(15);
    options.Http2.MaxConcurrentStreams = 200;
});
\```

Test:
\```bash
# HTTP/1.1
curl http://localhost:5100/health

# HTTP/2 over TLS
curl --http2 https://localhost:5101/api/data --insecure

# Verify protocol
curl -v --http2 https://localhost:5101/health --insecure 2>&1 | grep "HTTP/"
\```
```

- [ ] **Step 2: Commit**

```bash
git add docs/server/scenarios.md
git commit -m "docs: add real-world server scenario examples"
```

---

## Phase 6: Visual Redesign

### Task 20: Build CodeTabs.vue component

**Files:**
- Create: `docs/.vitepress/components/CodeTabs.vue`

- [ ] **Step 1: Create the tabbed code block component**

Write to `docs/.vitepress/components/CodeTabs.vue`:

```vue
<script setup lang="ts">
import { ref } from 'vue'

interface Tab {
    label: string
    language?: string
    code: string
}

const props = defineProps<{
    tabs: Tab[]
}>()

const activeIndex = ref(0)
</script>

<template>
    <div class="code-tabs">
        <div class="code-tabs-header">
            <button
                v-for="(tab, index) in tabs"
                :key="tab.label"
                :class="['code-tab-btn', { active: activeIndex === index }]"
                @click="activeIndex = index"
            >
                {{ tab.label }}
            </button>
        </div>
        <div class="code-tabs-body">
            <div
                v-for="(tab, index) in tabs"
                :key="tab.label"
                v-show="activeIndex === index"
                class="code-tab-panel"
            >
                <div class="language-{{ tab.language || 'csharp' }}">
                    <pre><code>{{ tab.code }}</code></pre>
                </div>
            </div>
        </div>
    </div>
</template>

<style scoped>
.code-tabs {
    border: 1px solid var(--vp-c-divider);
    border-radius: 8px;
    overflow: hidden;
    margin: 16px 0;
}

.code-tabs-header {
    display: flex;
    background: var(--vp-c-bg-soft);
    border-bottom: 1px solid var(--vp-c-divider);
}

.code-tab-btn {
    padding: 8px 16px;
    border: none;
    background: transparent;
    color: var(--vp-c-text-2);
    cursor: pointer;
    font-size: 14px;
    font-weight: 500;
    transition: color 0.2s, border-color 0.2s;
    border-bottom: 2px solid transparent;
}

.code-tab-btn:hover {
    color: var(--vp-c-text-1);
}

.code-tab-btn.active {
    color: var(--vp-c-brand-1);
    border-bottom-color: var(--vp-c-brand-1);
}

.code-tabs-body {
    background: var(--vp-code-block-bg);
}

.code-tab-panel pre {
    margin: 0;
    padding: 16px 24px;
    overflow-x: auto;
}

.code-tab-panel code {
    font-family: var(--vp-font-family-mono);
    font-size: 14px;
    line-height: 1.6;
    color: var(--vp-c-text-1);
}
</style>
```

- [ ] **Step 2: Commit**

```bash
git add docs/.vitepress/components/CodeTabs.vue
git commit -m "docs: add CodeTabs Vue component for tabbed code blocks"
```

---

### Task 21: Build HomePage.vue component

**Files:**
- Create: `docs/.vitepress/components/HomePage.vue`

- [ ] **Step 1: Create the custom homepage component**

Write to `docs/.vitepress/components/HomePage.vue`:

```vue
<script setup lang="ts">
const features = [
    {
        title: 'Multi-Protocol',
        description: 'HTTP/1.0, 1.1, 2 & 3 (QUIC) — automatic version negotiation, HPACK/QPACK compression, multiplexed streams.',
    },
    {
        title: 'Zero Allocation',
        description: 'Span<T>, Memory<byte>, and pooled buffers throughout. Zero GC pressure on the hot path.',
    },
    {
        title: 'Smart Retry & Cache',
        description: 'Idempotency-aware retries + in-memory LRU cache with ETag support. Built in, not bolted on.',
    },
    {
        title: 'Middleware & Routing',
        description: 'ASP.NET Core-style pipeline with Use/Run/Map. Entity gateway routes requests to Akka.NET actors.',
    },
    {
        title: 'Connection Pooling',
        description: 'Per-host pools with idle eviction, automatic reconnect, and configurable concurrency limits.',
    },
    {
        title: 'Standalone Server',
        description: 'Actor-based HTTP server with TCP/QUIC transport, supervisor hierarchy, and graceful shutdown.',
    },
]

const comparison = [
    { feature: 'HTTP/2 Multiplexing', httpClient: 'Partial', refit: 'Partial', flurl: 'No', turbo: 'Full' },
    { feature: 'HTTP/3 (QUIC)', httpClient: 'Partial', refit: 'No', flurl: 'No', turbo: 'Full' },
    { feature: 'Automatic Retries', httpClient: 'Polly needed', refit: 'Polly needed', flurl: 'No', turbo: 'Built-in' },
    { feature: 'HTTP Caching', httpClient: 'No', refit: 'No', flurl: 'No', turbo: 'Built-in' },
    { feature: 'Cookie Management', httpClient: 'Manual', refit: 'Manual', flurl: 'Manual', turbo: 'Automatic' },
    { feature: 'Backpressure', httpClient: 'No', refit: 'No', flurl: 'No', turbo: 'Akka.Streams' },
    { feature: 'Zero-alloc Internals', httpClient: 'Partial', refit: 'No', flurl: 'No', turbo: 'Full' },
    { feature: 'Channel-based API', httpClient: 'No', refit: 'No', flurl: 'No', turbo: 'Yes' },
]

const clientCode = `builder.Services.AddTurboHttpClient("api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRetry()
.WithCache()
.WithCookies()
.WithRedirect();

var client = factory.CreateClient("api");
var response = await client.SendAsync(request, ct);`

const serverCode = `builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);
});

app.MapTurboGet("/health", () => new { status = "ok" });
app.MapTurboGet("/users/{id}", (int id) =>
    new { id, name = "User " + id });

await app.RunAsync();`

import { ref } from 'vue'
const activeTab = ref<'client' | 'server'>('client')
</script>

<template>
    <div class="home-page">
        <!-- Hero -->
        <section class="hero">
            <div class="hero-content">
                <div class="hero-text">
                    <h1 class="hero-title">TurboHTTP</h1>
                    <p class="hero-tagline">High-Performance HTTP Client & Server for .NET</p>
                    <div class="hero-badges">
                        <span class="badge">Zero Alloc</span>
                        <span class="badge">HTTP/1–3 + QUIC</span>
                        <span class="badge">Backpressure</span>
                    </div>
                    <div class="hero-actions">
                        <a href="/getting-started/" class="action-btn primary">Get Started</a>
                        <a href="https://github.com/leberkas-org/TurboHTTP" class="action-btn secondary" target="_blank">GitHub</a>
                    </div>
                </div>
                <div class="hero-code">
                    <div class="code-header">
                        <button
                            :class="['tab', { active: activeTab === 'client' }]"
                            @click="activeTab = 'client'"
                        >Client</button>
                        <button
                            :class="['tab', { active: activeTab === 'server' }]"
                            @click="activeTab = 'server'"
                        >Server</button>
                    </div>
                    <pre class="code-block"><code>{{ activeTab === 'client' ? clientCode : serverCode }}</code></pre>
                </div>
            </div>
        </section>

        <!-- Features -->
        <section class="features">
            <h2 class="section-title">Features</h2>
            <div class="feature-grid">
                <div v-for="f in features" :key="f.title" class="feature-card">
                    <h3>{{ f.title }}</h3>
                    <p>{{ f.description }}</p>
                </div>
            </div>
        </section>

        <!-- Comparison -->
        <section class="comparison">
            <h2 class="section-title">vs. the Alternatives</h2>
            <div class="table-wrapper">
                <table>
                    <thead>
                        <tr>
                            <th>Feature</th>
                            <th>HttpClient</th>
                            <th>Refit</th>
                            <th>Flurl</th>
                            <th class="highlight">TurboHTTP</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="row in comparison" :key="row.feature">
                            <td>{{ row.feature }}</td>
                            <td>{{ row.httpClient }}</td>
                            <td>{{ row.refit }}</td>
                            <td>{{ row.flurl }}</td>
                            <td class="highlight">{{ row.turbo }}</td>
                        </tr>
                    </tbody>
                </table>
            </div>
        </section>

        <!-- Install -->
        <section class="install">
            <h2 class="section-title">Get Started</h2>
            <div class="install-code">
                <pre><code>dotnet add package TurboHTTP</code></pre>
            </div>
            <div class="install-links">
                <a href="/getting-started/" class="install-link">Getting Started</a>
                <a href="/client/" class="install-link">Client Docs</a>
                <a href="/server/" class="install-link">Server Docs</a>
            </div>
        </section>
    </div>
</template>

<style scoped>
.home-page {
    max-width: 1152px;
    margin: 0 auto;
    padding: 0 24px;
}

/* Hero */
.hero {
    padding: 64px 0 48px;
}

.hero-content {
    display: flex;
    gap: 48px;
    align-items: center;
}

.hero-text {
    flex: 1;
}

.hero-title {
    font-size: 48px;
    font-weight: 700;
    background: linear-gradient(135deg, #10b981, #8b5cf6);
    -webkit-background-clip: text;
    -webkit-text-fill-color: transparent;
    background-clip: text;
    margin: 0 0 8px;
    line-height: 1.1;
}

.hero-tagline {
    font-size: 20px;
    color: var(--vp-c-text-2);
    margin: 0 0 24px;
    line-height: 1.4;
}

.hero-badges {
    display: flex;
    gap: 8px;
    margin-bottom: 24px;
    flex-wrap: wrap;
}

.badge {
    padding: 4px 12px;
    border-radius: 12px;
    font-size: 13px;
    font-weight: 600;
    background: var(--vp-c-brand-soft);
    color: var(--vp-c-brand-1);
}

.hero-actions {
    display: flex;
    gap: 12px;
}

.action-btn {
    padding: 10px 24px;
    border-radius: 8px;
    font-size: 15px;
    font-weight: 600;
    text-decoration: none;
    transition: opacity 0.2s;
}

.action-btn:hover {
    opacity: 0.9;
}

.action-btn.primary {
    background: var(--vp-c-brand-1);
    color: white;
}

.action-btn.secondary {
    border: 1px solid var(--vp-c-divider);
    color: var(--vp-c-text-1);
}

/* Hero code block */
.hero-code {
    flex: 1;
    border-radius: 12px;
    overflow: hidden;
    border: 1px solid var(--vp-c-divider);
}

.code-header {
    display: flex;
    background: var(--vp-c-bg-soft);
    border-bottom: 1px solid var(--vp-c-divider);
}

.tab {
    padding: 8px 16px;
    border: none;
    background: transparent;
    color: var(--vp-c-text-2);
    cursor: pointer;
    font-size: 13px;
    font-weight: 500;
    border-bottom: 2px solid transparent;
}

.tab.active {
    color: var(--vp-c-brand-1);
    border-bottom-color: var(--vp-c-brand-1);
}

.code-block {
    margin: 0;
    padding: 20px 24px;
    background: var(--vp-code-block-bg);
    overflow-x: auto;
}

.code-block code {
    font-family: var(--vp-font-family-mono);
    font-size: 13px;
    line-height: 1.6;
    color: var(--vp-c-text-1);
}

/* Features */
.features {
    padding: 48px 0;
}

.section-title {
    font-size: 28px;
    font-weight: 700;
    text-align: center;
    margin: 0 0 32px;
    color: var(--vp-c-text-1);
}

.feature-grid {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 16px;
}

.feature-card {
    padding: 24px;
    border-radius: 12px;
    border: 1px solid var(--vp-c-divider);
    background: var(--vp-c-bg-soft);
    transition: border-color 0.2s;
}

.feature-card:hover {
    border-color: var(--vp-c-brand-1);
}

.feature-card h3 {
    margin: 0 0 8px;
    font-size: 16px;
    color: var(--vp-c-text-1);
}

.feature-card p {
    margin: 0;
    font-size: 14px;
    color: var(--vp-c-text-2);
    line-height: 1.5;
}

/* Comparison */
.comparison {
    padding: 48px 0;
}

.table-wrapper {
    overflow-x: auto;
}

.comparison table {
    width: 100%;
    border-collapse: collapse;
    font-size: 14px;
}

.comparison th,
.comparison td {
    padding: 12px 16px;
    text-align: left;
    border-bottom: 1px solid var(--vp-c-divider);
}

.comparison th {
    font-weight: 600;
    color: var(--vp-c-text-1);
    background: var(--vp-c-bg-soft);
}

.comparison td {
    color: var(--vp-c-text-2);
}

.comparison .highlight {
    color: var(--vp-c-brand-1);
    font-weight: 600;
}

/* Install */
.install {
    padding: 48px 0 64px;
    text-align: center;
}

.install-code {
    max-width: 400px;
    margin: 0 auto 24px;
}

.install-code pre {
    padding: 16px 24px;
    border-radius: 8px;
    background: var(--vp-code-block-bg);
    border: 1px solid var(--vp-c-divider);
}

.install-code code {
    font-family: var(--vp-font-family-mono);
    font-size: 15px;
    color: var(--vp-c-text-1);
}

.install-links {
    display: flex;
    justify-content: center;
    gap: 16px;
}

.install-link {
    padding: 8px 20px;
    border-radius: 8px;
    border: 1px solid var(--vp-c-divider);
    color: var(--vp-c-text-1);
    text-decoration: none;
    font-size: 14px;
    font-weight: 500;
    transition: border-color 0.2s, color 0.2s;
}

.install-link:hover {
    border-color: var(--vp-c-brand-1);
    color: var(--vp-c-brand-1);
}

/* Responsive */
@media (max-width: 768px) {
    .hero-content {
        flex-direction: column;
    }

    .hero-title {
        font-size: 36px;
    }

    .feature-grid {
        grid-template-columns: 1fr;
    }

    .install-links {
        flex-direction: column;
        align-items: center;
    }
}
</style>
```

- [ ] **Step 2: Commit**

```bash
git add docs/.vitepress/components/HomePage.vue
git commit -m "docs: add HomePage Vue component with hero, features, comparison"
```

---

### Task 22: Update homepage index.md to use custom layout

**Files:**
- Modify: `docs/index.md`

- [ ] **Step 1: Replace the entire index.md with custom layout**

Replace the content of `docs/index.md` with:

```markdown
---
layout: page
---

<HomePage />
```

This uses VitePress's `page` layout (no sidebar, no aside) and renders the custom `HomePage` component.

- [ ] **Step 2: Start dev server and verify the homepage renders**

Run from `docs/`:
```bash
npx vitepress dev
```

Open `http://localhost:5173/` in a browser. Verify:
- Gradient title "TurboHTTP" renders
- Stat badges appear
- Client/Server tabs toggle code blocks
- 6 feature cards in a grid
- Comparison table renders
- Install section with links
- Responsive: resize browser to mobile width

- [ ] **Step 3: Commit**

```bash
git add docs/index.md
git commit -m "docs: use custom HomePage layout for landing page"
```

---

### Task 23: Update custom.css with enhanced styles

**Files:**
- Modify: `docs/.vitepress/theme/custom.css`

- [ ] **Step 1: Add enhanced typography and content page styles**

Add to the end of `docs/.vitepress/theme/custom.css` (keep all existing styles):

```css
/* Enhanced content page styles */

/* Better code block styling */
.vp-doc div[class*='language-'] {
    border-radius: 8px;
    border: 1px solid var(--vp-c-divider);
}

.vp-doc div[class*='language-'] code {
    font-size: 13.5px;
}

/* Improved callout boxes */
.vp-doc .custom-block {
    border-radius: 8px;
    padding: 16px 20px;
}

.vp-doc .custom-block .custom-block-title {
    font-weight: 600;
}

/* Feature table styling */
.vp-doc table {
    border-radius: 8px;
    overflow: hidden;
}

.vp-doc table th {
    background: var(--vp-c-bg-soft);
    font-weight: 600;
}

/* Subtle heading anchors */
.vp-doc h2 {
    border-top: 1px solid var(--vp-c-divider);
    padding-top: 24px;
    margin-top: 48px;
}

/* Inline code styling */
.vp-doc :not(pre) > code {
    border-radius: 4px;
    padding: 2px 6px;
    font-size: 13px;
}
```

- [ ] **Step 2: Commit**

```bash
git add docs/.vitepress/theme/custom.css
git commit -m "docs: enhance content page CSS (code blocks, callouts, tables)"
```

---

## Phase 7: Polish

### Task 24: VitePress build verification

**Files:** None (verification only)

- [ ] **Step 1: Run a full VitePress build**

Run from `docs/`:
```bash
npx vitepress build
```

Expected: Build completes without errors. Watch for:
- Missing page warnings (all sidebar links should resolve)
- Dead link warnings
- Component registration errors

- [ ] **Step 2: Fix any broken links found during build**

If VitePress reports dead links, fix them. Common issues:
- Old `/quickstart/` or `/why/` links in pages not yet updated
- The migration link in `client/index.md` still pointing to `./migration` instead of `/getting-started/migration`

Search all markdown files for old paths:
```bash
grep -r "/quickstart/" docs/ --include="*.md"
grep -r "/why/" docs/ --include="*.md"
grep -r "./migration" docs/client/ --include="*.md"
```

Fix all occurrences.

- [ ] **Step 3: Run VitePress preview**

```bash
npx vitepress build && npx vitepress preview
```

Open `http://localhost:4173/` and verify:
- Homepage renders with all sections
- Navigation works (all 4 top-level items)
- Sidebar appears correctly for each section
- Architecture sidebar appears when navigating to `/architecture/` pages
- Getting Started has 5 sidebar items
- API has 6 sidebar items
- Client and Server have correct item counts
- Feature pages have cross-link callouts at the bottom
- Dark mode toggle works
- Mobile responsive (resize browser)

- [ ] **Step 4: Commit any remaining fixes**

```bash
git add -A docs/
git commit -m "docs: fix broken links and build warnings"
```

---

### Task 25: Final link audit

**Files:** None (verification only)

- [ ] **Step 1: Search for all remaining references to old paths or Kestrel framing**

```bash
# Old paths
grep -r "/quickstart" docs/ --include="*.md" --include="*.ts"
grep -r "/why/" docs/ --include="*.md" --include="*.ts"

# Kestrel framing (should only appear in the naming explanation callout)
grep -ri "built on kestrel\|uses kestrel\|kestrel integration\|via kestrel\|kestrel configuration" docs/ --include="*.md"

# Old API properties that don't exist
grep -r "MaxResponseContentBufferSize" docs/ --include="*.md"
grep -r "AllowEarlyData" docs/ --include="*.md"
grep -r "AllowServerPush" docs/ --include="*.md"
grep -r "MaxBatchWeight" docs/ --include="*.md"
grep -r "WithEntityKey" docs/ --include="*.md"
```

Expected: No matches for old paths. Kestrel references only in "About AddTurboKestrel" callout boxes. No matches for removed API properties.

- [ ] **Step 2: Fix any remaining issues and commit**

```bash
git add -A docs/
git commit -m "docs: final cleanup — remove stale references"
```
