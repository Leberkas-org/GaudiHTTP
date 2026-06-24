# Installation & Setup

## Requirements

- **.NET 10.0** or later
- **Akka.NET** is pulled in as a transitive dependency — no manual installation needed

## Install the Package

```bash
dotnet add package GaudiHTTP
```

Or add it to your `.csproj`:

```xml
<PackageReference Include="GaudiHTTP" Version="3.0.0-alpha.*" />
```

## Dependency Injection (Recommended)

Register GaudiHTTP in your `IServiceCollection`:

```csharp
using GaudiHTTP.Client;

var builder = WebApplication.CreateBuilder(args);

// Register a default client
builder.Services.AddGaudiHttpClient(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
});

var app = builder.Build();
```

Inject `IGaudiHttpClientFactory` into your services:

```csharp
public sealed class OrderService
{
    private readonly IGaudiHttpClient _client;

    public OrderService(IGaudiHttpClientFactory factory)
    {
        _client = factory.CreateClient();
    }

    public async Task<Order> GetOrderAsync(int id, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/orders/{id}");
        var response = await _client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Order>(ct);
    }
}
```

## Named Clients

Register multiple clients with different configurations:

```csharp
// Public API — caching enabled
builder.Services.AddGaudiHttpClient("public-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithCache();

// Internal service — aggressive retries
builder.Services.AddGaudiHttpClient("internal", options =>
{
    options.BaseAddress = new Uri("http://internal-service:8080");
})
.WithRetry(retry => { retry.MaxRetries = 5; });
```

Resolve by name:

```csharp
public sealed class GatewayService
{
    private readonly IGaudiHttpClient _publicApi;
    private readonly IGaudiHttpClient _internal;

    public GatewayService(IGaudiHttpClientFactory factory)
    {
        _publicApi = factory.CreateClient("public-api");
        _internal = factory.CreateClient("internal");
    }
}
```

## Typed Clients

Register a named client and resolve it directly as a typed `IGaudiHttpClient` subtype:

```csharp
builder.Services.AddGaudiHttpClient<OrderClient>(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRetry();
```

`TClient` must be a class with a constructor that accepts `IGaudiHttpClient` — the registration uses `ActivatorUtilities.CreateInstance<TClient>(sp, client)` to inject the named client at resolution time. Any class meeting this constructor requirement works (the generic constraint is `where TClient : class`, not `IGaudiHttpClient`).

## Fluent Builder API

Use the builder pattern to compose features:

```csharp
builder.Services.AddGaudiHttpClient("full-featured", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRedirect()              // follow redirects (default settings)
.WithRetry()                 // automatic retries
.WithCookies()               // automatic cookie management
.WithCache()                 // HTTP caching
.WithDecompression(true);    // gzip/deflate/brotli
```

## Minimal Example

A complete console application using the DI-based approach:

```csharp
using GaudiHTTP.Client;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddGaudiHttpClient(options =>
{
    options.BaseAddress = new Uri("https://jsonplaceholder.typicode.com");
});

var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<IGaudiHttpClientFactory>();
using var client = factory.CreateClient();

var request = new HttpRequestMessage(HttpMethod.Get, "/posts/1");
var response = await client.SendAsync(request, CancellationToken.None);

Console.WriteLine($"Status: {response.StatusCode}");
Console.WriteLine(await response.Content.ReadAsStringAsync());
```

::: warning
Always dispose the client when done to ensure connections are properly cleaned up.
:::

## Next Steps

- [Getting Started](./index) — basic usage patterns and feature overview
- [Configuration](./configuration) — all options explained in detail
- [API Reference](/api/) — full public API surface
