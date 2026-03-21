# Why TurboHttp?

.NET already ships `HttpClient`. It handles the common case well — single requests, basic headers, response deserialization. So why would you reach for something else?

TurboHttp is designed for situations where `HttpClient` alone isn't enough: high-throughput request pipelines, automatic retry and caching without Polly boilerplate, full cookie lifecycle management, and true HTTP/2 multiplexing — all built in, not bolted on.

## Feature Comparison

| Feature | HttpClient | Refit | Flurl | TurboHttp |
|---------|:----------:|:-----:|:-----:|:---------:|
| HTTP/1.0 | ✅ | ✅ | ✅ | ✅ |
| HTTP/1.1 | ✅ | ✅ | ✅ | ✅ |
| HTTP/2 Multiplexing | ⚠️ Partial | ⚠️ Partial | ❌ | ✅ Full |
| Automatic Retries | ❌ Polly needed | ❌ Polly needed | ❌ | ✅ Built-in |
| HTTP Caching | ❌ | ❌ | ❌ | ✅ Built-in |
| Cookie Management | ⚠️ Manual / CookieContainer | ⚠️ Manual | ⚠️ Manual | ✅ Automatic |
| Redirect Following | ✅ Basic | ✅ Basic | ✅ Basic | ✅ Full |
| Content Decompression | ✅ | ✅ | ✅ | ✅ |
| Connection Pooling | ✅ SocketsHttpHandler | ✅ via HttpClient | ✅ via HttpClient | ✅ Actor-based, per-host |
| Channel-based API | ❌ | ❌ | ❌ | ✅ |
| Backpressure | ❌ | ❌ | ❌ | ✅ Akka.Streams |
| Zero-alloc internals | ⚠️ Partial | ❌ | ❌ | ✅ Span/Memory throughout |
| Typed client interfaces | ❌ | ✅ | ❌ | ❌ |
| Fluent request builder | ❌ | ❌ | ✅ | ❌ |

> **⚠️ Partial** means the feature exists but has constraints — for example, HttpClient's HTTP/2 support requires .NET 5+ and TLS, and its cookie support relies on a shared `CookieContainer` that requires manual setup.

## When to Use TurboHttp

TurboHttp is a good fit when:

- **You're making many concurrent requests to the same host** — HTTP/2 multiplexing sends all requests over one TCP connection, eliminating connection setup overhead.
- **You need retry and caching without a separate library** — built-in idempotency-aware retries and an LRU cache with conditional request support work out of the box.
- **Cookie handling matters** — per-client `CookieJar` with automatic domain/path matching, attribute enforcement, and expiration handling.
- **You want a composable pipeline** — Akka.Streams stages let you insert signing, telemetry, or transformation logic without wrapping the client.
- **Throughput is critical** — `Span<T>`, `Memory<byte>`, and `IBufferWriter<byte>` are used throughout the encoding and decoding paths to avoid allocations.

## When NOT to Use TurboHttp

TurboHttp is not the right tool for every job. Be honest about the trade-offs:

- **You need typed client interfaces** — [Refit](https://github.com/reactiveui/refit) generates strongly typed HTTP clients from C# interfaces. TurboHttp has no equivalent; you work with `HttpRequestMessage` directly.
- **You need a fluent request builder** — [Flurl](https://flurl.dev/) provides a clean API for building URLs and requests inline. TurboHttp's API is lower-level.
- **You're already on Polly** — If your team is invested in Polly's retry and circuit-breaker policies, sticking with `HttpClient` + Polly is a reasonable choice. TurboHttp's built-in retry is simpler but not as composable.
- **You're making simple one-off requests** — `HttpClient.GetAsync(url)` is two words. TurboHttp requires a bit more setup. Use the simpler tool for simple problems.
- **You have no Akka.NET in your stack** — TurboHttp's I/O layer is built on Akka actors. It pulls in the Akka.NET dependency. If that's unwanted, a plain `HttpClient` is lighter.

## HttpClient vs TurboHttp: A Closer Look

### Retries

With `HttpClient` you typically add Polly:

```csharp
services.AddHttpClient("my-client")
    .AddTransientHttpErrorPolicy(p =>
        p.WaitAndRetryAsync(3, _ => TimeSpan.FromSeconds(1)));
```

With TurboHttp, retry is on by default for idempotent methods (GET, HEAD, PUT, DELETE). Configure or disable it per-client:

```csharp
var client = factory.CreateClient(options =>
{
    options.RetryPolicy = RetryPolicy.Default with { MaxRetries = 5 };
});
```

No Polly dependency, no handler registration, no strategy boilerplate.

### Caching

`HttpClient` has no built-in HTTP cache. You'd need a third-party library or write your own `DelegatingHandler`.

TurboHttp caches GET responses automatically — freshness evaluation, conditional requests (ETag/If-None-Match), and Vary header support included:

```csharp
// Caching is on by default. To tune it:
var client = factory.CreateClient(options =>
{
    options.CachePolicy = CachePolicy.Default with { MaxEntries = 500 };
});

// To disable caching entirely:
var client = factory.CreateClient(options =>
{
    options.CachePolicy = null;
});
```

### Typed Interfaces (Refit wins here)

If your API contract is defined in a C# interface, Refit is hard to beat:

```csharp
[Get("/users/{id}")]
Task<User> GetUserAsync(int id);
```

TurboHttp has no source generator and no interface-based client. You work with `HttpRequestMessage` and `HttpResponseMessage` directly, or through the channel-based API. If typed clients are your primary requirement, use Refit.

### Throughput

For scenarios where you're dispatching hundreds of requests to the same host, TurboHttp's HTTP/2 multiplexing and backpressure-aware channel API can sustain significantly higher throughput than sequential `HttpClient` calls:

```csharp
// Produce requests at full speed; TurboHttp handles pacing
var writer = client.Requests;
var reader = client.Responses;

await Parallel.ForEachAsync(requests, async (req, ct) =>
{
    await writer.WriteAsync(req, ct);
});

await foreach (var response in reader.ReadAllAsync())
{
    // process response
}
```

## Summary

| Scenario | Recommended |
|----------|-------------|
| Simple REST calls, small scale | `HttpClient` |
| Typed API client from interface | Refit |
| Fluent URL building | Flurl |
| High-throughput, HTTP/2, built-in caching + retry | **TurboHttp** |
| Need Polly circuit-breaker patterns | `HttpClient` + Polly |
