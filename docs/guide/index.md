# Getting Started

## Installation

Add TurboHttp to your .NET project:

```bash
dotnet add package TurboHttp
```

**Requirements:** .NET 10.0 or later.

## Basic Usage

### Simple Request

```csharp
using TurboHttp.Client;
using System.Net.Http;

await using var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.DefaultRequestVersion = HttpVersion.Version20;
});

var request = new HttpRequestMessage(HttpMethod.Get, "/users");
var response = await client.SendAsync(request);

Console.WriteLine($"Status: {response.StatusCode}");
var body = await response.Content.ReadAsStringAsync();
Console.WriteLine(body);
```

### Channel-based API

For high-throughput scenarios, use the channel-based API directly:

```csharp
await using var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
});

// Write requests
await client.RequestWriter.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/ping"));

// Read responses
var response = await client.ResponseReader.ReadAsync();
Console.WriteLine($"Status: {response.StatusCode}");
```

## Configuration

### HTTP Version

```csharp
var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");

    // Force HTTP/2
    options.DefaultRequestVersion = HttpVersion.Version20;

    // Or force HTTP/1.1
    options.DefaultRequestVersion = HttpVersion.Version11;
});
```

### Default Headers

```csharp
var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.DefaultRequestHeaders.Add("Authorization", "Bearer <token>");
    options.DefaultRequestHeaders.Add("Accept", "application/json");
});
```

### Per-Host Connection Limits

```csharp
var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.MaxConnectionsPerHost = 6; // default: 8
});
```

### Timeout and Cancellation

TurboHttp respects `CancellationToken` on every async call:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

var request = new HttpRequestMessage(HttpMethod.Get, "/data");
var response = await client.SendAsync(request, cts.Token);
```

## Next Steps

- [Architecture Overview](./architecture) — understand the layered design and Akka.Streams pipeline
- [Protocol Support](./protocols) — details on HTTP/1.0, HTTP/1.1, and HTTP/2 behaviour
