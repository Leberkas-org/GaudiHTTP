# End-to-End Scenarios

Here's what happens when you send a request with different HTTP versions. The details differ, but the pipeline stages are the same — enrichment, tracing, cookies, cache, encoding, network, decoding, decompression, cookie storage, cache storage, retries, redirects.

---

## HTTP/1.0 — Simple Request/Response

<ClientOnly>
  <LikeC4Diagram viewId="scenarioHttp10" />
</ClientOnly>

### Request Path

1. The application calls `SendAsync` on `ITurboHttpClient` with an `HttpRequestMessage` targeting HTTP/1.0.
2. `RequestEnricher` applies the base address and any default headers.
3. `TracingBidiStage` starts an activity span for observability.
4. `RedirectBidiStage` and `CookieBidiStage` inject matching cookies from `CookieJar`.
5. `CacheBidiStage` checks the cache — on a miss, the request continues.
6. `ContentEncodingBidiStage` compresses the request body if a compression policy is configured.
7. `Engine` routes the request to `Http10Engine`.
8. `Http10ConnectionStage` serialises the request to bytes with `Connection: close`.
9. `NetworkBufferBatchStage` coalesces outbound items into fewer writes.
10. `TcpConnectionStage` acquires a lease from `ConnectionPool.AcquireAsync()` and sends the bytes over TCP.

### Response Path

11. The server's response bytes arrive via TCP and flow through `TcpConnectionStage` into `Http10ConnectionStage`.
12. `Http10ConnectionStage` parses the HTTP/1.0 response (body length determined by `Content-Length` or EOF) and correlates it to the pending request.
13. `ContentEncodingBidiStage` decompresses the body if needed.
14. `CacheBidiStage` caches the response if it is cacheable.
15. `RetryBidiStage` passes the response through (no retry needed for a successful response).
16. `CookieBidiStage` stores any `Set-Cookie` headers.
17. `RedirectBidiStage` passes the response through (no redirect needed for a `200`).
18. `TracingBidiStage` closes the activity span, recording the final status code.
19. The final `HttpResponseMessage` is delivered to the application.

### Key Characteristic

After step 19, the TCP connection is closed. The next HTTP/1.0 request will go through the full connection setup again. There is no keep-alive feedback loop.

---

## HTTP/1.1 — Persistent Connection with Keep-Alive

<ClientOnly>
  <LikeC4Diagram viewId="scenarioHttp11" />
</ClientOnly>

HTTP/1.1 follows the same request/response path as HTTP/1.0 except for one critical difference: the connection can be **reused** after the response is delivered.

### Keep-Alive Handling

After `Http11ConnectionStage` decodes the response and correlates it to the pending request, it evaluates the `Connection` header internally:

- `Connection: keep-alive` (or HTTP/1.1 default) → the connection lease is returned to `ConnectionPool` for reuse
- `Connection: close` → the lease is released without returning it to the idle queue; the next request triggers a new connection

On **reuse**, the next request to the same host can skip connection setup entirely.

### Pipelining

`Http11ConnectionStage` uses a FIFO queue internally to correlate requests with responses, enabling HTTP/1.1 pipelining: multiple requests can be in-flight on the same connection simultaneously, and responses are matched to requests in order.

---

## HTTP/2 — Multiplexed Streams

<ClientOnly>
  <LikeC4Diagram viewId="scenarioHttp2" />
</ClientOnly>

HTTP/2 is fundamentally different from HTTP/1.x. A single TCP connection carries many concurrent logical **streams**, each identified by an odd integer stream ID assigned by the client.

### Request Framing

1. `Http20ConnectionStage` assigns the next available stream ID (1, 3, 5, …), HPACK-encodes the request headers into a `HEADERS` frame, and serialises the body (if any) into `DATA` frame(s).
2. `Http20ConnectionStage` applies connection-level and stream-level flow control — it will withhold frames if the server's receive window is exhausted.
3. `NetworkBufferBatchStage` coalesces outbound frame buffers into fewer, larger writes.
4. `TcpConnectionStage` sends the frames over TCP (injecting the HTTP/2 connection preface on the first connection).

### Connection-Level Frames

While request/response streams are active, `Http20ConnectionStage` also handles:

- **`SETTINGS`** — initial and updated connection parameters; acknowledges server `SETTINGS` with `SETTINGS ACK`
- **`PING`** — round-trip latency measurement; responds to server `PING` with `PING ACK`
- **`WINDOW_UPDATE`** — flow control credits; emitted automatically as the consumer reads response data
- **`GOAWAY`** — graceful shutdown; after receiving `GOAWAY`, no new streams are opened on this connection

### Response Assembly

5. Raw bytes from TCP flow through `TcpConnectionStage` into `Http20ConnectionStage`.
6. `Http20ConnectionStage` parses the bytes into HTTP/2 frames (handling partial frames across TCP boundaries), routes connection-level frames to internal handlers, assembles per-stream `HEADERS` + `DATA` frames into an `HttpResponseMessage`, HPACK-decodes response headers, and correlates each assembled response back to its pending request using the stream ID.
7. The response continues through `ContentEncodingBidiStage`, `CacheBidiStage`, `RetryBidiStage`, `CookieBidiStage`, and `RedirectBidiStage` — the same response chain as HTTP/1.x.

### Stream ID Exhaustion

Client-side stream IDs are 31-bit odd integers. When the maximum (`2^31 - 1`) is reached, the connection sends `GOAWAY` and a new connection is established. This is handled transparently by `HostConnections`.

---

## HTTP/3 — Multiplexed over QUIC

<ClientOnly>
  <LikeC4Diagram viewId="scenarioHttp3" />
</ClientOnly>

HTTP/3 replaces TCP with **QUIC**, a UDP-based transport that provides built-in encryption and independent stream delivery. Each request uses its own QUIC stream, so a lost packet on one stream does not block other in-flight requests.

### Request Framing

1. `Http30ConnectionStage` QPACK-encodes the request headers into a `HEADERS` frame, and the body (if any) into `DATA` frame(s).
2. `Http30ConnectionStage` manages connection-level concerns — `SETTINGS`, `GOAWAY`, and stream lifecycle.
3. `NetworkBufferBatchStage` coalesces outbound items into fewer, larger writes.
4. `QuicConnectionStage` acquires a QUIC connection from the pool and sends the bytes over the network.

### Connection-Level Frames

While request/response streams are active, `Http30ConnectionStage` handles:

- **`SETTINGS`** — connection parameters exchanged at startup
- **`GOAWAY`** — graceful shutdown; after receiving `GOAWAY`, no new streams are opened on this connection

### Response Assembly

5. Raw bytes from QUIC flow through `QuicConnectionStage` into `Http30ConnectionStage`.
6. `Http30ConnectionStage` parses the bytes into HTTP/3 frames, routes connection-level frames to internal handlers, assembles per-stream `HEADERS` + `DATA` frames into an `HttpResponseMessage`, and QPACK-decodes response headers.
7. The response continues through `ContentEncodingBidiStage`, `CacheBidiStage`, `RetryBidiStage`, `CookieBidiStage`, and `RedirectBidiStage` — the same response chain as HTTP/1.x and HTTP/2.

### Key Differences from HTTP/2

| | HTTP/2 | HTTP/3 |
|---|--------|--------|
| **Transport** | TCP + TLS | QUIC (UDP + built-in TLS) |
| **Head-of-line blocking** | Yes — one lost TCP packet stalls all streams | No — each QUIC stream is independent |
| **Header compression** | HPACK | QPACK (adapted for out-of-order delivery) |
| **Connection preface** | Required (`PRI * HTTP/2.0...`) | Not needed — QUIC handles this |
