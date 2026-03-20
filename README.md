<div align="center">
  <img src="docs/logo/logo.svg" alt="TurboHttp" width="200" />
  <h1>TurboHttp</h1>
  <p><strong>High-performance HTTP client for .NET — built on Akka.Streams with full RFC compliance.</strong></p>

  [![Build](https://github.com/st0o0/TurboHttp/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/st0o0/TurboHttp/actions/workflows/build-and-release.yml)
  [![NuGet](https://img.shields.io/nuget/v/TurboHttp.svg)](https://www.nuget.org/packages/TurboHttp)
  [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
</div>

---

## Features

- **HTTP/1.0, HTTP/1.1, and HTTP/2** — full protocol support with automatic version negotiation
- **Akka.Streams pipeline** — backpressure-aware, reactive processing with zero actor hops on the data path
- **RFC-compliant** — 2,435 tests across 7 RFCs; 100% coverage of implemented sections
- **Connection pooling** — per-host actor hierarchy with idle eviction and reconnect with exponential backoff
- **Redirect handling** — RFC 9110 §15.4: 301/302/303/307/308 with correct method rewriting and loop detection
- **Retry logic** — idempotency-based retries with `Retry-After` support (RFC 9110 §9.2)
- **Cookie management** — RFC 6265: domain/path matching, `Secure`/`HttpOnly`/`SameSite`, `Max-Age`/`Expires`
- **HTTP caching** — RFC 9111: LRU cache with `Vary` support, conditional requests, and freshness evaluation
- **HPACK compression** — RFC 7541: Huffman coding, dynamic table management, sensitive-header protection
- **Content decoding** — gzip, deflate, and brotli decompression
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

## RFC Compliance

| RFC | Standard | Coverage | Unit Tests | Stream Tests |
|-----|----------|----------|------------|--------------|
| RFC 1945 | HTTP/1.0 | 100% | 233 | 41 |
| RFC 9112 | HTTP/1.1 Message Framing | 100% | 374 | 97 |
| RFC 9113 | HTTP/2 | 100% | 545 | 180 |
| RFC 7541 | HPACK Header Compression | 100% | 419 | 8 |
| RFC 9110 | HTTP Semantics | 100% | 123 | 55 |
| RFC 6265 | HTTP Cookies | 100% | 66 | 12 |
| RFC 9111 | HTTP Caching | 100% | 75 | 28 |
| **Total** | | | **1,835** | **421** |

See [RFC_COVERAGE.md](RFC_COVERAGE.md) for the full compliance matrix, gap table, and per-file test mapping.

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
