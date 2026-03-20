<div align="center">
  <img src="docs/logo/logo.svg" alt="TurboHttp" width="200" />
  <h1>TurboHttp</h1>
  <p><strong>High-performance HTTP client for .NET — built on Akka.Streams with automatic retries, caching, cookies, and HTTP/2 multiplexing.</strong></p>

  [![Build](https://github.com/st0o0/TurboHttp/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/st0o0/TurboHttp/actions/workflows/build-and-release.yml)
  [![NuGet](https://img.shields.io/nuget/v/TurboHttp.svg)](https://www.nuget.org/packages/TurboHttp)
  [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
</div>

---

## Features

- **HTTP/1.0, HTTP/1.1, and HTTP/2** — full protocol support with automatic version negotiation
- **Automatic retries** — idempotent methods (GET, PUT, DELETE) are retried automatically; respects `Retry-After` headers; POST is never retried
- **Built-in HTTP caching** — in-memory LRU cache with `Vary` support, conditional requests (ETag, Last-Modified), and freshness evaluation
- **Cookie management** — automatic cookie storage and injection; domain/path matching, `Secure`/`HttpOnly`/`SameSite`, `Max-Age`/`Expires`
- **Redirect following** — 301/302/303/307/308 with correct method rewriting, body preservation, loop detection, and cross-origin safety
- **Content decoding** — automatic gzip, deflate, and Brotli decompression
- **Connection pooling** — per-host pools with idle eviction and automatic reconnect with exponential backoff
- **HTTP/2 multiplexing** — multiple requests over a single TCP connection with header compression and flow control
- **Akka.Streams pipeline** — backpressure-aware, reactive processing with zero actor hops on the data path
- **Zero-allocation internals** — `Span<T>`, `IBufferWriter<byte>`, and `System.Threading.Channels` throughout

---

## Installation

```bash
dotnet add package TurboHttp
```

Requires **.NET 10.0** or later.

---

## Quick Start

```csharp
using TurboHttp.Client;
using System.Net.Http;

// Create and configure the client
await using var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.DefaultRequestVersion = HttpVersion.Version20;
});

// Send a request
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

---

## Architecture

TurboHttp uses a layered design where **actors manage lifecycle** and **channels carry data** — no bytes ever touch an actor mailbox:

```
Client Layer       ITurboHttpClient (SendAsync / channel API)
      ↓
Streams Layer      Akka.Streams GraphStages — Engine, ConnectionStage, Protocol Engines
      ↓
Protocol Layer     Encoders/Decoders, HPACK, RedirectHandler, RetryEvaluator, CookieJar
      ↓
I/O Layer          Actors: PoolRouterActor → HostPoolActor → ConnectionActor (lifecycle)
                   Data:   ConnectionStage ←→ Channel<byte> ←→ ClientByteMover ←→ TCP
      ↓
Network            TCP
```

For interactive architecture diagrams, see the [documentation site](https://st0o0.github.io/TurboHttp/).

---

## Documentation

Full documentation — including feature guides, architecture overview, API reference, and a comparison with other HTTP clients — is available at **[https://st0o0.github.io/TurboHttp/](https://st0o0.github.io/TurboHttp/)**.

---

## Building from Source

```bash
# Restore and build
dotnet restore ./src/TurboHttp.sln
dotnet build --configuration Release ./src/TurboHttp.sln

# Run all tests
dotnet test ./src/TurboHttp.sln

# Run benchmarks
dotnet run --configuration Release ./src/TurboHttp.Benchmarks/TurboHttp.Benchmarks.csproj
```

---

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) for branch naming conventions, PR requirements, how to run tests locally, and recommended branch protection settings.

---

## License

TurboHttp is licensed under the [MIT License](LICENSE).
