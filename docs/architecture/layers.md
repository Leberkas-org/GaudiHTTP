# Architectural Layers

TurboHttp is composed of four layers. The container view shows all four layers and how they relate to each other and the outside world.

## Container View

<ClientOnly>
  <LikeC4Diagram viewId="turbohttp" :height="560" />
</ClientOnly>

The four containers are:

| Container | Description |
|-----------|-------------|
| **Client** | Public API surface — `ITurboHttpClient`, `SendAsync`, and the channel-based request/response interface |
| **Streams** | Akka.Streams `GraphStage` pipeline — version demultiplexer, per-version protocol engines, request enrichment, and all middleware stages (cookies, caching, compression, redirect, retry) |
| **Protocol** | Pure protocol logic — encoders, decoders, HPACK, `CookieJar`, `HttpCacheStore`, `RedirectHandler`, `RetryEvaluator` |
| **I/O** | Hybrid lifecycle + data-path layer — actor hierarchy manages connection pooling; `System.Threading.Channels` carry bytes with zero actor hops |

---

## Client Layer

<ClientOnly>
  <LikeC4Diagram viewId="clientLayer" :height="400" />
</ClientOnly>

`ITurboHttpClient` exposes two interaction modes:

- **`SendAsync(HttpRequestMessage, CancellationToken)`** — standard `HttpClient`-style request/response, suitable for most callers
- **Channel API** — direct `ChannelWriter<HttpRequestMessage>` / `ChannelReader<HttpResponseMessage>` for high-throughput producer/consumer pipelines

The client layer creates and materialises the Akka.Streams graph. All configuration (base address, default HTTP version, default headers, per-host connection limits) is applied here before requests enter the stream.

---

## Streams Layer

<ClientOnly>
  <LikeC4Diagram viewId="streamsLayer" :height="600" />
</ClientOnly>

The Streams layer is the heart of TurboHttp. It is a single composable Akka.Streams graph that processes every request through the following stages (in order):

**Request chain:**

1. `RequestEnricherStage` — applies `BaseAddress`, `DefaultRequestVersion`, `DefaultRequestHeaders`
2. `CookieBidiStage` — injects matching cookies from `CookieJar` into the `Cookie` header
3. `CacheBidiStage` — checks `HttpCacheStore`; returns a cached response immediately on hit, bypassing the network
4. `Engine` — demultiplexes by HTTP version and routes to `Http10Engine`, `Http11Engine`, or `Http20Engine`

**Response chain (after the network):**

5. `DecompressionBidiStage` — decompresses gzip/deflate/brotli response bodies
6. `CookieBidiStage` — parses `Set-Cookie` headers and stores cookies in `CookieJar`
7. `CacheBidiStage` — stores cacheable responses in `HttpCacheStore`
8. `RetryBidiStage` — retries idempotent requests on transient failures
9. `RedirectBidiStage` — follows redirects with method rewriting and loop detection

**Key cross-cutting stages:**

| Stage | Purpose |
|-------|---------|
| `ConnectionStage` | TCP connection wrapper; communicates with the I/O actor pool via `ConnectionHandle` |
| `Http1XCorrelationStage` | FIFO request-response matching for HTTP/1.x pipelined connections |
| `Http20CorrelationStage` | Stream-ID-based matching for HTTP/2 multiplexed streams |

---

## I/O Layer

<ClientOnly>
  <LikeC4Diagram viewId="ioLayer" :height="520" />
</ClientOnly>

The I/O layer uses a **hybrid pattern** — actors manage connection lifecycle while data travels through lock-free channels.

### Actor Hierarchy (lifecycle only)

```
PoolRouter
  └── HostPool  (one per host:port)
        └── ConnectionActor  (one per TCP connection)
              └── ClientRunner → ClientByteMover
```

- **`PoolRouter`** — receives `EnsureHost` messages; routes to the correct `HostPool`, creating one if needed
- **`HostPool`** — maintains the pool of connections for a single host; enforces `PerHostConnectionLimiter`; handles reconnect scheduling and idle eviction
- **`ConnectionActor`** — owns a single TCP socket; creates `Channel<(IMemoryOwner<byte>, int)>` pairs; spawns `ClientRunner` on connect; sends `ConnectionReady(ConnectionHandle)` back to `HostPool`
- **`ClientRunner`** — per-connection actor that starts `ClientByteMover` tasks and signals lifecycle events (connected, disconnected, error)
- **`ClientByteMover`** — three static async tasks per connection: TCP→Pipe, Pipe→InboundChannel, OutboundChannel→TCP

### Data Path (zero actor hops)

```
ConnectionStage ←→ OutboundWriter / InboundReader ←→ ClientByteMover ←→ TCP socket
```

`ConnectionHandle` is a plain record containing `ChannelWriter<byte>` (outbound) and `ChannelReader<byte>` (inbound). `ConnectionStage` writes and reads these channels directly — no actor mailbox is ever in the hot path.

### Why Hybrid?

Actors are excellent for managing shared, mutable state (pool membership, reconnect backoff, idle timers). They are poor at high-frequency data movement. `System.Threading.Channels` gives the data path lock-free, zero-copy throughput without the overhead of actor message passing.
