# Troubleshooting & FAQ

## Frequently Asked Questions

### Do I need to install Akka.NET separately?

No. Akka.NET is a transitive dependency of GaudiHTTP — it is pulled in automatically when you install the package.

### Can I use GaudiHTTP alongside HttpClient?

Yes. Both can coexist in the same application. Use `IHttpClientFactory` for your existing services and `IGaudiHttpClientFactory` for new ones. See [Gradual Migration](/getting-started/migration#gradual-migration).

### Is GaudiHTTP thread-safe?

Yes. `IGaudiHttpClient` is fully thread-safe. Multiple threads can call `SendAsync` concurrently. The channel-based API (`Requests` / `Responses`) supports concurrent producers and consumers.

### How do I disable a feature?

Features are opt-in via the builder API. Simply don't call the corresponding builder method:

```csharp
// No retries, no caching, no redirects — just register without builder extensions
builder.Services.AddGaudiHttpClient("bare", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
});
```

### Does GaudiHTTP support HTTPS?

Yes. TLS is handled automatically for `https://` URIs. Configure TLS options via `TurboClientOptions`:

```csharp
options.EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
options.ClientCertificates = new X509CertificateCollection { cert };
```

### What HTTP versions are supported?

HTTP/1.0, HTTP/1.1, HTTP/2, and HTTP/3 (QUIC). Set the version on the client:

```csharp
var client = factory.CreateClient("my-api");
client.DefaultRequestVersion = HttpVersion.Version20; // or Version11, Version10, Version30
```

Per-request overrides are also supported via `HttpRequestMessage.Version`.

---

## Common Issues

### Connection Refused / Timeout

**Symptom:** `SendAsync` throws a timeout or connection exception.

**Causes:**

1. Server is not reachable — verify the URL and network
2. `ConnectTimeout` too low — increase it:
   ```csharp
   options.ConnectTimeout = TimeSpan.FromSeconds(30);
   ```
3. DNS resolution failure — check hostname spelling
4. Firewall blocking the port

### Too Many Redirects

**Symptom:** You receive a 3xx redirect response instead of the final destination response — GaudiHTTP stopped following redirects after hitting the configured limit.

**Fix:** The server is returning a redirect loop. Either fix the server or increase the redirect limit via the builder:

```csharp
builder.Services.AddGaudiHttpClient("my-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRedirect(redirect => { redirect.MaxRedirects = 20; });
```

To debug, remove the `.WithRedirect()` call entirely and inspect the redirect responses manually.

### HTTPS to HTTP Downgrade Blocked

**Symptom:** A redirect from `https://` to `http://` is not followed — GaudiHTTP returns the 3xx redirect response instead of following it.

**Cause:** A server redirected from `https://` to `http://`, which GaudiHTTP blocks by default for security.

**Fix (if the downgrade is intentional):**

```csharp
builder.Services.AddGaudiHttpClient("my-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRedirect(redirect => { redirect.AllowHttpsToHttpDowngrade = true; });
```

### POST Requests Are Not Retried

**By design.** POST, PATCH, and other non-idempotent methods are never automatically retried — retrying them could create duplicate resources or cause unintended side effects. Only idempotent methods (GET, HEAD, OPTIONS, TRACE, PUT, DELETE) are retried automatically.

This behaviour **cannot be disabled or bypassed** via `RetryOptions`. The idempotency check is baked into the retry evaluator and cannot be configured away.

If you need to retry POST in your application, implement retry logic in your own code:

```csharp
var maxRetries = 3;
for (var attempt = 1; attempt <= maxRetries; attempt++)
{
    try
    {
        using var response = await client.SendAsync(postRequest, ct);
        return response;
    }
    catch (HttpRequestException ex) when (attempt < maxRetries)
    {
        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct);
    }
}
```

The built-in `.WithRetry()` handles idempotent method detection and backoff automatically — use it for GET, PUT, DELETE, etc., but implement custom retry logic for POST if needed.

### High Memory Usage

**Possible causes:**

1. **Cache too large** — reduce `MaxEntries` or `MaxBodySize` when registering:
   ```csharp
   .WithCache(c => { c.MaxEntries = 100; c.MaxBodySize = 10 * 1024 * 1024; })
   ```
2. **Response bodies not disposed** — always dispose `HttpResponseMessage` when done:
   ```csharp
   using var response = await client.SendAsync(request, ct);
   ```
3. **CookieJar accumulating** — clear periodically if needed by calling `Clear()` on the `ICookieStore` you passed to `.WithCookies(store)`:
   ```csharp
   store.Clear(); // store is the ICookieStore you provided to .WithCookies(store)
   ```

### HTTP/2 Connection Failures

**Symptom:** HTTP/2 request fails, HTTP/1.1 works.

**Possible causes:**

1. Server doesn't support HTTP/2 — use `HttpVersionPolicy.RequestVersionOrLower` to fall back
2. Cleartext HTTP/2 (h2c) not supported by server — use HTTPS
3. TLS ALPN negotiation failed — check server TLS configuration

```csharp
var client = factory.CreateClient("my-api");
client.DefaultRequestVersion = HttpVersion.Version20;
client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower; // graceful fallback
```

### Channel Backpressure (WriteAsync Blocks)

**Symptom:** `Requests.WriteAsync` hangs.

**Cause:** The outbound channel is full — the connection cannot send requests as fast as you produce them. This is **correct behaviour** (backpressure).

**Fixes:**

1. Use HTTP/2 for multiplexing (concurrent streams over one connection)
2. Increase `MaxConnectionsPerServer` for HTTP/1.1:
   ```csharp
   options.Http1.MaxConnectionsPerServer = 16;
   ```
3. Ensure consumer task is actively reading responses to unblock the producer

### Stale Cache Responses

**Symptom:** Getting old data despite server changes.

**Fixes:**

1. Force revalidation on a specific request:
   ```csharp
   request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
   ```
2. Bypass cache entirely:
   ```csharp
   request.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
   ```
3. Reduce cache size when registering:
   ```csharp
   .WithCache(c => { c.MaxEntries = 100; })
   ```

---

## Debugging Tips

### Enable Akka Logging

GaudiHTTP uses Akka's logging infrastructure. To see connection lifecycle events:

```csharp
// In your Akka configuration
akka.loglevel = DEBUG
akka.actor.debug.lifecycle = on
```

### Inspect Connection State

The actor hierarchy provides connection pool visibility. Use Akka's built-in monitoring to see:

- Active connections per host
- Idle connection count
- Reconnect attempts

### Capture Wire Traffic

For protocol-level debugging, capture TCP traffic with Wireshark or a proxy:

```csharp
// Route through a debugging proxy (e.g., Fiddler, mitmproxy)
options.BaseAddress = new Uri("http://localhost:8080"); // proxy address
```

For HTTP/2, use a tool that understands binary framing (e.g., Wireshark with HTTP/2 dissector).
