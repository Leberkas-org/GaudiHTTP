# API Reference

GaudiHTTP's public API is organized into client, server, and feature configuration.

## Client API

| Type | Description | Reference |
|------|-------------|-----------|
| `IGaudiHttpClientFactory` | Creates named client instances | [Client API](./client) |
| `IGaudiHttpClient` | The HTTP client — `SendAsync` and channel-based API | [Client API](./client) |
| `TurboClientOptions` | Connection, TLS, proxy, and protocol settings | [Client Options](./client-options) |
| `Http1ClientOptions` / `Http2ClientOptions` / `Http3ClientOptions` | Per-protocol tuning | [Client Options](./client-options) |
| `RetryOptions` / `CacheOptions` / `RedirectOptions` | Feature configuration | [Feature Options](./feature-options) |
| Builder extensions (`.WithRetry()`, `.WithCache()`, etc.) | Fluent feature composition | [Feature Options](./feature-options) |

## Server API

| Type | Description | Reference |
|------|-------------|-----------|
| `UseGaudiHttp()` | Server registration on `builder.Host` (standalone HTTP server) | [Server API](./server) |
| `GaudiServerOptions` | Endpoints, protocols, timeouts | [Server API](./server) |
| `Http1ServerOptions` / `Http2ServerOptions` / `Http3ServerOptions` | Per-protocol tuning | [Server API](./server) |
| `app.MapGet/Post/Put/Delete/Patch()` | Standard ASP.NET Core route registration | [Server API](./server) |
| ASP.NET Core middleware | Standard middleware pipeline | [Server API](./server) |
| Standard `HttpContext` | Request/response context via `IFeatureCollection` | [Server API](./server) |

## DI Registration

### Client

```csharp
// Named client
builder.Services.AddGaudiHttpClient("api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRetry()
.WithCache();

// Default (unnamed) client
builder.Services.AddGaudiHttpClient(options => { ... });

// Typed client
builder.Services.AddGaudiHttpClient<IMyApiClient>(options => { ... });
```

### Server

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseGaudiHttp(options =>
{
    options.ListenLocalhost(5100);
});

var app = builder.Build();
await app.RunAsync();
```

## Quick Links

- [Client Configuration Guide](/client/configuration)
- [Server Configuration Guide](/server/configuration)
- [HTTP/2 & Multiplexing](/client/http2)
- [HTTP/3 & QUIC](/client/http3)
- [Automatic Retries](/client/retries)
- [HTTP Caching](/client/caching)
- [Cookies](/client/cookies)
- [Redirects](/client/redirects)
