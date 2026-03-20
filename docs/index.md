---
layout: home

hero:
  name: TurboHttp
  text: High-Performance HTTP Client for .NET
  tagline: Built on Akka.Streams with full RFC compliance — HTTP/1.0, HTTP/1.1, and HTTP/2.
  image:
    src: /logo/logo.svg
    alt: TurboHttp
  actions:
    - theme: brand
      text: Get Started
      link: /guide/
    - theme: alt
      text: View on GitHub
      link: https://github.com/st0o0/TurboHttp

features:
  - icon: ⚡
    title: HTTP/1.0, 1.1, and HTTP/2
    details: Full protocol support with automatic version negotiation. HPACK compression, flow control, and multiplexed streams for HTTP/2.

  - icon: 🔄
    title: Akka.Streams Pipeline
    details: Backpressure-aware, reactive processing with zero actor hops on the data path. System.Threading.Channels for lock-free I/O.

  - icon: ✅
    title: RFC-Compliant
    details: 2,435 tests across 7 RFCs — RFC 1945, RFC 9112, RFC 9113, RFC 7541, RFC 9110, RFC 9111, and RFC 6265.

  - icon: 🔗
    title: Connection Pooling
    details: Per-host actor hierarchy with idle eviction and reconnect with exponential backoff. Configurable per-host concurrency limits.

  - icon: 🔀
    title: Redirect & Retry
    details: RFC 9110 §15.4 redirect handling with correct method rewriting and loop detection. Idempotency-based retries with Retry-After support.

  - icon: 🍪
    title: Cookie Management
    details: RFC 6265 domain/path matching, Secure/HttpOnly/SameSite attributes, Max-Age/Expires handling, and thread-safe CookieJar.

  - icon: 📦
    title: HTTP Caching
    details: RFC 9111 LRU cache with Vary support, conditional requests (If-None-Match, If-Modified-Since), and freshness evaluation.

  - icon: 🚀
    title: Zero-Allocation Internals
    details: Span<T>, IBufferWriter<byte>, and ReadOnlyMemory<byte> throughout. Pre-allocated buffers for encoding; stateful decoders with no heap pressure.
---
