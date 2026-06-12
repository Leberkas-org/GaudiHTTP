# When to Use TurboHTTP

TurboHTTP is not a drop-in "faster HttpClient/Kestrel". It is an HTTP stack built on Akka.Streams
whose strengths are **streaming, backpressure, large payloads under concurrency, and actor
integration** — and whose trade-off is per-request overhead on tiny, latency-critical requests.
This page summarizes where each side of the stack wins, based on the benchmark suite
(BenchmarkDotNet, loopback, 2026-06).

## TL;DR

| Your workload | Recommendation |
|---|---|
| Many small GETs, lowest possible latency | HttpClient / Kestrel |
| Large request bodies (uploads) under concurrency | **TurboHTTP client** (H2/H3: up to 2–3.5× HttpClient) |
| Upload-heavy server endpoints (HTTP/1.1) | **TurboServer** (+10–34 % vs Kestrel) |
| Streaming, SSE, backpressure end-to-end | **TurboHTTP (both sides)** |
| Actor-based backends (Akka.NET) | **TurboServer** — shares your `ActorSystem` |
| Bulk request pipelines (fire thousands, drain results) | **TurboHTTP client channel API** |

## As a Client

### Where it wins

- **Concurrent uploads over HTTP/2 and HTTP/3.** With many in-flight POSTs, the multiplexed
  upload path clearly beats `SocketsHttpHandler`: at 512–4096 concurrent 10 KB uploads the
  benchmark shows **+12 % to +58 % (H2)** and **+123 % to +247 % (H3)** throughput, with up to
  **84 % fewer allocations** (H2, CL=4096). Tail latency follows: p99 is 40–70 % lower in these
  scenarios.
- **HTTP/1.1 uploads at scale** run close to HttpClient (within ~30–40 % at high concurrency)
  with bounded memory — the request body pump is backpressured against the socket instead of
  buffering whole bodies.
- **Resilience built into the pipeline.** Retries, reconnect with request replay, redirects,
  cookies, HTTP caching, and content encoding are stream stages, not handler wrappers — and all
  of it is observable through permanent `Servus.Senf` tracing.
- **The channel API** (`client.Requests` / `client.Responses`) turns the client into a
  backpressured pipeline: write thousands of requests, drain responses as they complete. Ideal
  for crawlers, batch syncs, and fan-out jobs where aggregate throughput matters, not
  per-request latency.

### Where HttpClient is the better tool

- **Single-request latency on light GETs.** A lone ~3 B GET costs ~150–160 µs vs HttpClient's
  ~74 µs; light-GET fan-out at very high concurrency is also slower (H2/H3 light concurrent).
- **The channel API has a latency floor** (~1.3–1.6 ms per isolated request) from its
  stream-materialization hops — it amortizes over bulk work, not single calls.

## As a Server

### Where it wins

- **HTTP/1.1 upload endpoints.** 1 MB POSTs run **+10 % to +34 %** faster than Kestrel
  (sequential and CL=1 concurrent; +10–20 % at CL=64/256 sequential).
- **HTTP/2 / HTTP/3 request handling at parity.** Plaintext/JSON/Fortunes sequential are within
  ±5–15 % of Kestrel across protocols; several H2 concurrent scenarios (plaintext, JSON) are
  ahead at p95/p99.
- **Streaming responses with real backpressure.** Return an Akka Streams `Source` (SSE, long
  downloads) and flow control runs end-to-end — a slow client slows the producer instead of
  growing a buffer.
- **Actor integration.** TurboServer reuses your `ActorSystem` from DI; HTTP connections and
  domain actors share supervision, dispatchers, and tracing (see [Scenarios](/scenarios)).

### Where Kestrel is the better tool

- **Small-response throughput/latency records.** Plaintext/JSON-style endpoints are ~6–16 %
  slower at p50 and allocate more per request (managed allocations are roughly 3–4× Kestrel's
  2.7 KB; native/pooled buffers excluded on both sides).
- **Very high fan-out on HTTP/3.** Light-request concurrency over QUIC currently trails Kestrel
  significantly (-50 % to -74 %) — a known limitation of the shared pipeline, being worked on.

## In Combination

Running TurboHTTP on both ends pays off when the *pipeline* is the product:

- **Service-to-service with large payloads.** TurboHTTP client → TurboServer keeps uploads
  backpressured on both sides; neither end buffers whole bodies, so memory stays flat under
  load spikes.
- **End-to-end streaming.** An Akka Streams `Source` on the server feeds an Akka Streams
  consumer on the client — one flow-controlled graph across the network, including SSE.
- **Gateways and proxies.** Forward-proxy and CONNECT tunneling are supported; combined with
  the channel API this makes backpressured relay/aggregation services straightforward.
- **One ActorSystem everywhere.** Client stages, server connections, and your domain actors
  share dispatchers, supervision, and `Servus.Senf` tracing categories — a single operational
  surface from socket to business logic.

## Benchmark Context

Numbers above come from the repo's benchmark suite (`TurboHTTP.Benchmarks`): localhost loopback,
BenchmarkDotNet, HTTP/1.1 + h2c cleartext, HTTP/3 with self-signed TLS, run 2026-06. Loopback
isolates protocol-stack overhead and exaggerates per-request costs relative to real networks —
over WAN latencies, the gaps on light requests shrink while the streaming/backpressure advantages
remain. Memory figures count managed allocations only. Re-run with
`dotnet run -c Release --project TurboHTTP.Benchmarks` to reproduce on your hardware.
