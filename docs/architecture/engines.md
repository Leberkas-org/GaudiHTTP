# Protocol Engines

The `Engine` demultiplexes requests by HTTP version and routes each request to the appropriate per-version engine. Each engine is a self-contained Akka.Streams sub-graph composed of a **unified ConnectionStage** (which handles encoding, decoding, and request/response correlation internally) and a **NetworkBufferBatchStage** (which coalesces outbound items into fewer, larger writes). The engine's output connects to a transport stage (`TcpConnectionStage` or `QuicConnectionStage`) that manages the actual network connection.

---

## HTTP/1.0 Engine

<ClientOnly>
  <LikeC4Diagram viewId="http10Engine" :height="440" />
</ClientOnly>

HTTP/1.0 uses a **close-then-respond** model. Each connection handles exactly one request, then closes.

**Internal composition:**

```
HttpRequestMessage → Http10ConnectionStage → NetworkBufferBatchStage → [TcpConnectionStage] → TCP
TCP → [TcpConnectionStage] → Http10ConnectionStage → HttpResponseMessage
```

| Component | Role |
|-----------|------|
| `Http10ConnectionStage` | Unified stage: serialises request to wire bytes (sets `Connection: close`), parses the HTTP/1.0 response, and correlates request/response (FIFO, depth 1) |
| `NetworkBufferBatchStage` | Coalesces consecutive outbound network buffers into fewer, larger writes to reduce syscalls |
| `TcpConnectionStage` | TCP transport — acquires a connection lease from the pool, reads/writes bytes |

**Notable behaviours:**

- No keep-alive — every request opens and closes its own TCP connection
- No chunked transfer encoding
- Response body length determined by `Content-Length` header or connection close (EOF)

---

## HTTP/1.1 Engine

<ClientOnly>
  <LikeC4Diagram viewId="http11Engine" :height="480" />
</ClientOnly>

HTTP/1.1 adds **persistent connections** and **keep-alive control**. The unified connection stage handles encoding, decoding, correlation, and keep-alive evaluation internally.

**Internal composition:**

```
HttpRequestMessage → Http11ConnectionStage → NetworkBufferBatchStage → [TcpConnectionStage] → TCP
TCP → [TcpConnectionStage] → Http11ConnectionStage → HttpResponseMessage
```

| Component | Role |
|-----------|------|
| `Http11ConnectionStage` | Unified stage: serialises request (adds `Host`, `Connection`, `Transfer-Encoding: chunked` as needed), parses HTTP/1.1 responses (handles chunked decoding), correlates request/response (FIFO, depth > 1 enables pipelining), and evaluates keep-alive signals |
| `NetworkBufferBatchStage` | Coalesces consecutive outbound network buffers into fewer, larger writes — correctly handles interleaved control items (connection reuse signals) by flushing the buffer before forwarding them |
| `TcpConnectionStage` | TCP transport with connection reuse — returns leases to the pool on keep-alive, requests new connections on close |

**Keep-alive handling:**

After decoding each response, `Http11ConnectionStage` evaluates the `Connection` header internally:
- `Connection: keep-alive` (or HTTP/1.1 default) → the connection lease is returned to `ConnectionPool` for reuse
- `Connection: close` → the lease is released without returning it to the idle queue; the next request triggers a new connection

**Pipelining:**

`Http11ConnectionStage` uses a FIFO queue internally to correlate requests with responses, enabling HTTP/1.1 pipelining: multiple requests can be in-flight on the same connection, and responses are matched to requests in order.

---

## HTTP/2 Engine

<ClientOnly>
  <LikeC4Diagram viewId="http2Engine" :height="520" />
</ClientOnly>

HTTP/2 provides **stream multiplexing** — many logical requests share a single TCP connection, each assigned a unique stream ID.

**Internal composition:**

```
HttpRequestMessage → Http20ConnectionStage → NetworkBufferBatchStage → [TcpConnectionStage] → TCP
TCP → [TcpConnectionStage] → Http20ConnectionStage → HttpResponseMessage
```

| Component | Role |
|-----------|------|
| `Http20ConnectionStage` | Central unified stage: allocates client stream IDs (1, 3, 5, …), HPACK-encodes request headers and emits `HEADERS` + `DATA` frames, handles frame encoding/decoding (9-byte frame header + payload), manages connection-level frames (`SETTINGS`, `PING`, `WINDOW_UPDATE`, `GOAWAY`), tracks connection and stream-level flow control windows, assembles per-stream `HEADERS` + `DATA` frames into `HttpResponseMessage`, and correlates responses by stream ID |
| `NetworkBufferBatchStage` | Coalesces consecutive outbound frame buffers into fewer, larger writes — reducing syscall count under concurrent multiplexed streams; control items are flushed through immediately to preserve frame ordering |
| `TcpConnectionStage` | TCP transport — emits the HTTP/2 connection preface on first connect |

**HPACK header compression:**

`HpackEncoder` and `HpackDecoder` maintain synchronised dynamic tables. Sensitive headers (`Authorization`, `Cookie`) are automatically marked `NeverIndex`. The dynamic table size is negotiated via `SETTINGS_HEADER_TABLE_SIZE` during the connection preface exchange.

**Flow control:**

`Http20ConnectionStage` tracks both **connection-level** and **stream-level** window sizes. It emits `WINDOW_UPDATE` frames when the consumer reads data, preventing the remote server from stalling. The Akka.Streams backpressure demand signal is translated into HTTP/2 flow-control credits.

---

## HTTP/3 Engine

<ClientOnly>
  <LikeC4Diagram viewId="http3Engine" :height="520" />
</ClientOnly>

HTTP/3 runs over **QUIC** instead of TCP. Each request uses an independent QUIC stream, which eliminates the head-of-line blocking that affects HTTP/2 over a single TCP connection.

**Internal composition:**

```
HttpRequestMessage → Http30ConnectionStage → NetworkBufferBatchStage → [QuicConnectionStage] → QUIC
QUIC → [QuicConnectionStage] → Http30ConnectionStage → HttpResponseMessage
```

| Component | Role |
|-----------|------|
| `Http30ConnectionStage` | Central unified stage: QPACK-encodes request headers, emits `HEADERS` + `DATA` frames over QUIC streams, handles frame encoding/decoding using QUIC variable-length encoding, manages connection-level frames (`SETTINGS`, `GOAWAY`), handles stream multiplexing and lifecycle, assembles per-stream frames into `HttpResponseMessage`, and QPACK-decodes response headers |
| `NetworkBufferBatchStage` | Coalesces consecutive outbound items into fewer, larger writes |
| `QuicConnectionStage` | QUIC transport — acquires a QUIC connection from the pool, writes/reads bytes over QUIC streams |

**QPACK header compression:**

QPACK is the HTTP/3 equivalent of HPACK, adapted for QUIC's out-of-order delivery. `QpackEncoder` and `QpackDecoder` maintain synchronised dynamic tables and communicate updates via dedicated encoder/decoder instruction streams.

**No head-of-line blocking:**

Unlike HTTP/2 where a single lost TCP packet can stall all streams, HTTP/3's QUIC transport delivers each stream independently. A lost packet on one stream does not affect other in-flight requests.
