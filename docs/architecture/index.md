# Architecture Overview

TurboHttp is a high-performance HTTP client library for .NET built on Akka.Streams. It implements HTTP/1.0, HTTP/1.1, and HTTP/2 across four composable layers.

<ClientOnly>
  <LikeC4Diagram viewId="index" :height="520" />
</ClientOnly>

## System Boundaries

The diagram above shows the three actors in the system:

- **User** — any .NET application that creates and sends `HttpRequestMessage` objects
- **TurboHttp** — the library itself: four layers (Client, Streams, Protocol, I/O) encapsulated behind `ITurboHttpClient`
- **TCP Network** — the remote server; TurboHttp manages all TCP socket lifecycle internally

## Four-Layer Architecture

| Layer | Location | Responsibility |
|-------|----------|----------------|
| **Client** | `TurboHttp/Client/` | Public API — `ITurboHttpClient`, `SendAsync`, channel-based request/response |
| **Streams** | `TurboHttp/Streams/` | Akka.Streams `GraphStage` pipeline — version routing, encode/decode, correlation |
| **Protocol** | `TurboHttp/Protocol/` | Pure protocol logic — encoders, decoders, HPACK, cookies, caching, redirects, retries |
| **I/O** | `TurboHttp/IO/` | Hybrid: actors for lifecycle management, `System.Threading.Channels` for data |

Data flows **top-to-bottom** through the layers. Lifecycle management (connection pooling, reconnect, idle eviction) flows **bottom-up** via the actor hierarchy.

## Key Design Decisions

### Backpressure End-to-End

Every stage in the pipeline is a backpressure-aware Akka.Streams `GraphStage`. Demand propagates from the response consumer all the way back to the TCP reader — no unbounded buffers, no silent drops.

### Zero-Copy Data Path

The I/O layer uses `System.Threading.Channels` and `System.IO.Pipelines` to move bytes from TCP to the stream pipeline without copying through actor mailboxes. `ClientByteMover` runs three static async tasks per connection (TCP→Pipe, Pipe→InboundChannel, OutboundChannel→TCP), keeping the hot path allocation-free.

### Correctness by Design

Every protocol decision in TurboHttp is driven by the relevant HTTP specification. Encoding, decoding, caching freshness, redirect method rewriting, retry idempotency, cookie attribute handling, and HPACK header compression are all implemented as pure, independently testable logic — separate from the I/O and streaming infrastructure.

## Explore Further

- [**Layers**](./layers) — Container view and per-layer details
- [**Engines**](./engines) — Per-protocol engine internals
- [**Pipeline Flow**](./pipeline) — Full pipeline with feedback loops
- [**Scenarios**](./scenarios) — End-to-end request walkthroughs
