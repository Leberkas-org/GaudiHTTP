# When to Use TurboHTTP

TurboHTTP is not a drop-in "faster HttpClient/Kestrel". It is an HTTP stack built on Akka.Streams
whose strengths are **HTTP/2 multiplexing, streaming, backpressure, and actor integration** — and
whose trade-off is per-request overhead on tiny, latency-critical requests and a heavier cold start.
This page summarizes where each side of the stack wins, based on measured BenchmarkDotNet results
(Ryzen 7 5800X, .NET 10.0.8, loopback, 2026-06-21).

## TL;DR

| Your workload | Recommendation |
|---|---|
| Many small GETs, lowest possible latency | HttpClient / Kestrel |
| HTTP/2 server endpoints (plaintext, JSON) | **TurboServer** (1.4–1.5× Kestrel) |
| Concurrent downloads over HTTP/2 or HTTP/3 | **TurboHTTP client** (2–3.5× HttpClient) |
| HTTP/1.1 pipelined requests on a single connection | **TurboHTTP client** (up to 4.7× HttpClient) |
| Streaming, SSE, backpressure end-to-end | **TurboHTTP (both sides)** |
| Actor-based backends (Akka.NET) | **TurboServer** — shares your `ActorSystem` |
| Bulk request pipelines (fire thousands, drain results) | **TurboHTTP client channel API** |
| HTTP/3 (QUIC) at any scale | HttpClient / Kestrel (TurboHTTP H3 is 2–7× slower) |

## As a Client

### Where it wins

- **HTTP/1.1 pipelining on a single connection.** At 256 concurrent requests over one connection,
  TurboHTTP delivers **4.7× the throughput** (73K vs 15K req/s) of HttpClient. At 64 concurrent
  it is 1.5× faster. This makes it ideal for connection-constrained scenarios and serial
  keep-alive workloads.
- **HTTP/2 and HTTP/3 concurrent downloads.** Downloading 1 MB payloads across 32 connections,
  TurboHTTP is **2.3× faster on H2** (2,727 vs 1,199 req/s) and **3.5× faster on H3** (613 vs
  176 req/s). For 8 MB payloads the advantage holds: **2.4× on H2** and **2.9× on H3**. The
  streams-based body consumption handles flow-controlled data more efficiently than
  SocketsHttpHandler.
- **HTTP/1.1 concurrent light requests at moderate scale.** At 512 concurrent light GETs,
  TurboHTTP is **1.6× faster** (66K vs 42K req/s) than HttpClient.
- **HTTP/2 single-connection multiplexing.** At 64 concurrent requests on one H2 connection,
  TurboHTTP delivers **1.5× the throughput** (49K vs 33K req/s).
- **Resilience built into the pipeline.** Retries, reconnect with request replay, redirects,
  cookies, HTTP caching, and content encoding are stream stages, not handler wrappers — and all
  of it is observable through permanent `Servus.Senf` tracing.
- **The channel API** (`client.Requests` / `client.Responses`) turns the client into a
  backpressured pipeline: write thousands of requests, drain responses as they complete. Ideal
  for crawlers, batch syncs, and fan-out jobs where aggregate throughput matters, not
  per-request latency.

### Where HttpClient is the better tool

- **Single-request latency.** A warm light GET costs **114 µs vs HttpClient's 67 µs** on H1.1
  (~47 µs GraphInterpreter overhead), 123 vs 77 µs on H2, 228 vs 180 µs on H3.
- **Cold start.** First request takes **6.4 ms vs 480 µs** (13× slower) on H1.1/H2, allocating
  ~3 MB for actor system and streams graph materialization vs HttpClient's 33 KB.
- **Very high concurrency (4096+).** TurboHTTP's SendAsync API currently crashes at 4096
  concurrent requests across all protocols and at 512 for HTTP/3.
- **HTTP/3 (QUIC) generally.** Single-connection H3 is **4.5–7.3× slower** than HttpClient.
  This is a known transport-layer limitation being worked on.
- **HTTP/1.1 concurrent downloads.** At 32 connections downloading 1 MB, HttpClient is **3.1×
  faster** (11,413 vs 3,692 req/s) — the connection pool management overhead currently hurts
  on H1.1 download workloads.

## As a Server

### Where it wins

- **HTTP/2 plaintext and JSON at high concurrency.** At 256 concurrent requests, TurboServer
  delivers **1.5× Kestrel's throughput on plaintext** (80K vs 54K req/s) and **1.4× on JSON**
  (79K vs 57K req/s). At 64 concurrent it is 1.2–1.3× faster. HTTP/2 multiplexing is
  TurboServer's sweet spot.
- **HTTP/1.1 at near-parity.** Plaintext/JSON/Fortunes are within 5–10% of Kestrel on H1.1
  across all concurrency levels — competitive enough for most workloads.
- **Streaming responses with real backpressure.** Return an Akka Streams `Source` (SSE, long
  downloads) and flow control runs end-to-end — a slow client slows the producer instead of
  growing a buffer.
- **Actor integration.** TurboServer reuses your `ActorSystem` from DI; HTTP connections and
  domain actors share supervision, dispatchers, and tracing (see [Scenarios](/scenarios)).

### Where Kestrel is the better tool

- **HTTP/3 (QUIC) — significantly.** TurboServer is **1.4–3.9× slower** than Kestrel across
  all H3 workloads (JSON concurrent @256: 29K vs 114K req/s = 26% of Kestrel).
- **Larger response bodies on HTTP/2.** The Fortunes benchmark (larger HTML responses) shows a
  dramatic **4.2× regression** at 256 concurrent H2 requests (22K vs 92K req/s), compared to
  plaintext/JSON where TurboServer leads. This points to a body-write or serialization
  bottleneck specific to larger response payloads.
- **Per-request allocations.** TurboServer allocates roughly **2.5–3× more** per request than
  Kestrel (6.9 KB vs 2.6 KB on H1.1 plaintext). Kestrel pools its HttpContext, feature
  collections, and header dictionaries more aggressively.
- **Uploads at scale.** Upload endpoints are 1.3–1.4× slower on H1.1/H2.

## In Combination

Running TurboHTTP on both ends pays off when the *pipeline* is the product:

- **HTTP/2 service-to-service.** TurboHTTP client's H2 download advantage (2.3×) combined with
  TurboServer's H2 serving advantage (1.4–1.5×) makes a compelling end-to-end H2 story.
- **End-to-end streaming.** An Akka Streams `Source` on the server feeds an Akka Streams
  consumer on the client — one flow-controlled graph across the network, including SSE.
- **Gateways and proxies.** Forward-proxy and CONNECT tunneling are supported; combined with
  the channel API this makes backpressured relay/aggregation services straightforward.
- **One ActorSystem everywhere.** Client stages, server connections, and your domain actors
  share dispatchers, supervision, and `Servus.Senf` tracing categories — a single operational
  surface from socket to business logic.

## Benchmark Context

Numbers above come from the repo's benchmark suite (`TurboHTTP.Benchmarks`): Ryzen 7 5800X
(8C/16T), .NET 10.0.8, BenchmarkDotNet v0.15.8, localhost loopback, HTTP/1.1 + h2c cleartext,
HTTP/3 with self-signed TLS, measured 2026-06-21 on branch `feat/dispatcher-analysis` after 12+
optimization commits. Loopback isolates protocol-stack overhead and exaggerates per-request costs
relative to real networks — over WAN latencies, the gaps on light requests shrink while the
streaming/backpressure/multiplexing advantages remain. Memory figures count managed allocations
only. Re-run with `dotnet run -c Release --project TurboHTTP.Benchmarks` to reproduce on your
hardware.
