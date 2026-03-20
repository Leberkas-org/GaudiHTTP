# Architecture Overview

TurboHttp is structured in four layers. Data flows top-to-bottom; lifecycle management flows bottom-up via an actor hierarchy.

## Layered Design

```
Client Layer (TurboHttp/Client/)
    ITurboHttpClient — channel-based request/response API
         ↓
Streams Layer (TurboHttp/Streams/)
    Akka.Streams GraphStages — Engine, ConnectionStage, Protocol Engines
         ↓
Protocol Layer (TurboHttp/Protocol/)
    Encoders/Decoders, HPACK, RedirectHandler, RetryEvaluator, CookieJar
         ↓
I/O Layer (TurboHttp/IO/) — Hybrid: actors for lifecycle, Channels for data
    ┌─ Lifecycle (actor hierarchy) ──────────────────────────────┐
    │  PoolRouterActor → HostPoolActor → ConnectionActor         │
    └────────────────────────────────────────────────────────────┘
    ┌─ Data path (zero actor hops) ──────────────────────────────┐
    │  ConnectionStage ←→ Channel<byte> ←→ ClientByteMover ←→ TCP│
    └────────────────────────────────────────────────────────────┘
         ↓
Network (TCP)
```

## Client Layer

`ITurboHttpClient` exposes two interaction modes:

- **`SendAsync`** — familiar `HttpClient`-style request/response
- **Channel API** — direct access to `ChannelWriter<HttpRequestMessage>` and `ChannelReader<HttpResponseMessage>` for producer/consumer pipelines

## Streams Layer

Built entirely on Akka.Streams `GraphStage`. Each stage is a bounded, backpressure-aware unit. The `Engine` demultiplexes requests by HTTP version and routes them to the appropriate per-version engine (`Http10Engine`, `Http11Engine`, `Http20Engine`).

**Key stages:**

| Stage | Purpose |
|-------|---------|
| `RequestEnricherStage` | Applies `BaseAddress`, `DefaultRequestVersion`, `DefaultRequestHeaders` |
| `ExtractOptionsStage` | Splits `HttpRequest(Options, Message)` into transport config + request message |
| `Http11EncoderStage` / `Http11DecoderStage` | Serialize/deserialize HTTP/1.1 messages |
| `Http20ConnectionStage` | Bidirectional flow control, SETTINGS/PING/GOAWAY handling |
| `Http20StreamStage` | Assembles HTTP/2 frames into `HttpResponseMessage` |
| `CorrelationHttp1XStage` | FIFO request-response matching for HTTP/1.x |
| `CorrelationHttp20Stage` | Stream-ID-based request-response matching for HTTP/2 |

## Protocol Layer

Pure, stateless logic with no I/O dependencies:

- **Encoders** — serialize `HttpRequestMessage` to bytes using `ref Span<byte>` for zero-allocation output
- **Decoders** — stateful parsers that handle partial frames across TCP boundaries via a `_remainder` buffer
- **HPACK** (`HpackEncoder`, `HpackDecoder`, `HpackDynamicTable`, `HuffmanCodec`) — synchronized dynamic tables between encoder and decoder
- **RedirectHandler** — RFC 9110 §15.4 with correct method rewriting, HTTPS→HTTP protection, and loop detection
- **RetryEvaluator** — idempotency-based retries with `Retry-After` parsing (RFC 9110 §9.2)
- **CookieJar** — RFC 6265 domain/path matching with thread-safe access
- **HttpCacheStore** — RFC 9111 LRU cache with `Vary` support and conditional request building

## I/O Layer — Hybrid Architecture

The I/O layer uses a **hybrid pattern**: actors manage connection lifecycle; data flows through `System.Threading.Channels` with zero actor mailbox hops.

**Actor hierarchy (lifecycle only):**

```
PoolRouterActor
  └── HostPoolActor (one per host)
        └── ConnectionActor (one per TCP connection)
```

- `PoolRouterActor` — routes `EnsureHost` to per-host actors
- `HostPoolActor` — pools connections, enforces `PerHostConnectionLimiter`, handles reconnect/idle eviction
- `ConnectionActor` — owns TCP socket lifecycle, creates `Channel<byte>` pairs, spawns `ClientRunner`

**Data path (zero actor hops):**

```
ConnectionStage ←→ Channel<byte> ←→ ClientByteMover ←→ TCP socket
```

`ConnectionHandle` bundles `OutboundWriter` and `InboundReader` channels. `ConnectionStage` writes/reads directly without touching any actor mailbox. `ClientByteMover` runs three static async tasks per connection: TCP→Pipe, Pipe→InboundChannel, OutboundChannel→TCP.

## Design Principles

- **Separation of lifecycle and data** — actors own state and supervise failures; channels carry bytes
- **Backpressure end-to-end** — Akka.Streams demand propagation from response consumer back to TCP reader
- **Zero allocation on the hot path** — `Span<T>`, `IBufferWriter<byte>`, pooled `IMemoryOwner<byte>`
- **RFC compliance first** — every protocol decision maps to a specific RFC section with tests
