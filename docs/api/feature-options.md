# Feature Options and Builders

Feature options configure optional features and are applied via the builder API, not through `GaudiClientOptions`. All `With*` methods accept an optional configuration delegate; calling them without arguments enables the feature with its defaults. See [Configuration guide](/client/configuration) for builder usage.

## RetryOptions

```csharp
public sealed class RetryOptions
{
    public int MaxRetries { get; set; } = 3;
    public bool RespectRetryAfter { get; set; } = true;
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MaxRetries` | `3` | Number of retry attempts for idempotent requests |
| `RespectRetryAfter` | `true` | Honor `Retry-After` header in responses |

```csharp
// Default retries (3 attempts)
builder.Services.AddGaudiHttpClient("api", ...).WithRetry();

// Aggressive retry
builder.Services.AddGaudiHttpClient("api", ...)
    .WithRetry(r => { r.MaxRetries = 5; r.RespectRetryAfter = false; });
```

See [Automatic Retries guide](/client/retries) for which methods and status codes are retried.

---

## CacheOptions

```csharp
public sealed class CacheOptions
{
    public int MaxEntries { get; set; } = 1000;
    public long MaxBodySize { get; set; } = 50 * 1024 * 1024;     // 50 MiB
    public long MaxTotalSize { get; set; } = 256 * 1024 * 1024;   // 256 MiB
    public bool SharedCache { get; set; }
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MaxEntries` | `1000` | Max number of responses in the cache |
| `MaxBodySize` | `50 * 1024 * 1024` (50 MiB) | Max body size of a single stored response; larger responses are not cached |
| `MaxTotalSize` | `256 * 1024 * 1024` (256 MiB) | Max total size of all cached response bodies combined; least-recently-used entries are evicted when exceeded |
| `SharedCache` | `false` | Whether this is a shared cache (affecting `Cache-Control` directives) |

```csharp
// Enable caching with defaults
builder.Services.AddGaudiHttpClient("api", ...).WithCache();

// Smaller cache for constrained environments
builder.Services.AddGaudiHttpClient("api", ...)
    .WithCache(c => { c.MaxEntries = 100; c.MaxBodySize = 5 * 1024 * 1024; });

// Custom store shared across clients
var sharedStore = new MyCustomCacheStore();  // implement ICacheStore
builder.Services.AddGaudiHttpClient("api", ...).WithCache(sharedStore);
```

See [HTTP Caching guide](/client/caching) for freshness rules and conditional requests.

---

## RedirectOptions

```csharp
public sealed class RedirectOptions
{
    public int MaxRedirects { get; set; } = 10;
    public bool AllowHttpsToHttpDowngrade { get; set; }
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MaxRedirects` | `10` | Max number of redirects to follow |
| `AllowHttpsToHttpDowngrade` | `false` | Allow HTTPS → HTTP redirects (security risk) |

```csharp
// Follow redirects with defaults
builder.Services.AddGaudiHttpClient("api", ...).WithRedirect();

// Custom limit
builder.Services.AddGaudiHttpClient("api", ...).WithRedirect(r => { r.MaxRedirects = 3; });

// Disable redirect following (don't call .WithRedirect())
```

::: warning
`AllowHttpsToHttpDowngrade` should never be enabled in production. It exists for compatibility with legacy servers and testing only.
:::

See [Redirects guide](/client/redirects) for method rewriting and security details.

---

## CompressionOptions

```csharp
public sealed class CompressionOptions
{
    public string Encoding { get; set; } = "gzip";
    public long MinBodySize { get; set; } = 1024;
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `Encoding` | `"gzip"` | Compression algorithm ("gzip", "br", "deflate") |
| `MinBodySize` | `1024` | Don't compress bodies smaller than this |

```csharp
// Request compression with Brotli for large bodies
builder.Services.AddGaudiHttpClient("api", ...)
    .WithRequestCompression(c => { c.Encoding = "br"; c.MinBodySize = 4096; });

// Response decompression is enabled by default (AutomaticDecompression = true).
// WithDecompression() is only needed to explicitly disable it:
// builder.Services.AddGaudiHttpClient("api", ...).WithDecompression(enabled: false);
```

See [Content Encoding guide](/client/content-encoding) for request compression and Expect: 100-continue.

---

## Expect100Options

```csharp
public sealed class Expect100Options
{
    public long MinBodySize { get; set; } = 1024;
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MinBodySize` | `1024` | Only use Expect: 100-continue for bodies >= this size |

```csharp
// Enable 100-continue for bodies > 8 KiB
builder.Services.AddGaudiHttpClient("api", ...)
    .WithExpectContinue(e => { e.MinBodySize = 8 * 1024; });
```

See [Content Encoding guide](/client/content-encoding) for Expect: 100-continue details.

---

## Cookies

Cookies are configured via the builder, not through an options class:

```csharp
// Enable cookies with the built-in CookieJar
builder.Services.AddGaudiHttpClient("api", ...).WithCookies();

// Custom cookie storage
var customStore = new MyCookieStore();
builder.Services.AddGaudiHttpClient("api", ...).WithCookies(customStore);
```

Cookies are automatically extracted from `Set-Cookie` headers, stored by domain, and injected into subsequent requests. See [Cookies guide](/client/cookies) for domain matching and expiration rules.

---

## Builder Extension Methods

All feature options are configured via the `IGaudiHttpClientBuilder` interface:

```csharp
public static class GaudiHttpClientBuilderExtensions
{
    IGaudiHttpClientBuilder WithCookies(this IGaudiHttpClientBuilder builder);
    IGaudiHttpClientBuilder WithCookies(this IGaudiHttpClientBuilder builder, ICookieStore store);
    
    IGaudiHttpClientBuilder WithCache(this IGaudiHttpClientBuilder builder, Action<CacheOptions>? configure = null);
    IGaudiHttpClientBuilder WithCache(this IGaudiHttpClientBuilder builder, ICacheStore store, Action<CacheOptions>? configure = null);
    
    IGaudiHttpClientBuilder WithRetry(this IGaudiHttpClientBuilder builder, Action<RetryOptions>? configure = null);
    
    IGaudiHttpClientBuilder WithRedirect(this IGaudiHttpClientBuilder builder, Action<RedirectOptions>? configure = null);
    
    IGaudiHttpClientBuilder WithDecompression(this IGaudiHttpClientBuilder builder, bool enabled = true);
    
    IGaudiHttpClientBuilder WithRequestCompression(this IGaudiHttpClientBuilder builder, Action<CompressionOptions>? configure = null);
    
    IGaudiHttpClientBuilder WithExpectContinue(this IGaudiHttpClientBuilder builder, Action<Expect100Options>? configure = null);
    
    IGaudiHttpClientBuilder AddHandler<T>(this IGaudiHttpClientBuilder builder) where T : GaudiHandler;
    
    IGaudiHttpClientBuilder UseRequest(this IGaudiHttpClientBuilder builder, Func<HttpRequestMessage, HttpRequestMessage> transform);
    
    IGaudiHttpClientBuilder UseResponse(this IGaudiHttpClientBuilder builder, Func<HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> transform);
}
```

---

## Custom Handlers

The `GaudiHandler` base class provides a hook for custom request/response middleware:

```csharp
public abstract class GaudiHandler
{
    public virtual HttpRequestMessage ProcessRequest(HttpRequestMessage request) => request;
    public virtual HttpResponseMessage ProcessResponse(HttpRequestMessage original, HttpResponseMessage response) => response;
}
```

Custom handlers are registered via the builder:

```csharp
// Define a custom handler
public class AuthHeaderHandler : GaudiHandler
{
    public override HttpRequestMessage ProcessRequest(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetToken());
        return request;
    }
}

// Register it
builder.Services.AddGaudiHttpClient("api", ...)
    .AddHandler<AuthHeaderHandler>();
```

For request/response transformations, use `UseRequest` and `UseResponse` for inline lambdas:

```csharp
builder.Services.AddGaudiHttpClient("api", ...)
    .UseRequest(req => { req.Headers.Add("X-Custom", "value"); return req; });
```

See [Configuration guide](/client/configuration) for integration patterns and handler composition.

---

## Extension Points

These types are part of the public API and can be customized:

| Type | Purpose | Guide |
|------|---------|-------|
| `ICookieStore` | Cookie storage and injection — implement and pass to `.WithCookies(store)` | [Cookies](/client/cookies) |
| `ICacheStore` | Cache backend (extends `IDisposable`) — implement and pass to `.WithCache(store)` | [Caching](/client/caching) |
| `GaudiHandler` | Custom request/response middleware — register via `.AddHandler<T>()` | [Configuration](/client/configuration) |

See [Configuration guide](/client/configuration) for integration patterns.
