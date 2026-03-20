# Protocol Support

TurboHttp implements HTTP/1.0, HTTP/1.1, and HTTP/2 with full RFC compliance.

## HTTP/1.0 — RFC 1945

HTTP/1.0 uses a simple request/response model with one request per TCP connection by default.

**Supported features:**

- All request methods (GET, POST, PUT, DELETE, HEAD, OPTIONS, PATCH)
- Status line and header parsing
- `Content-Length`-delimited bodies
- Explicit `Connection: keep-alive` opt-in for connection reuse
- `Transfer-Encoding` not supported (HTTP/1.0 does not define chunked encoding)

**Encoder/Decoder:**

- `Http10Encoder` — serializes `HttpRequestMessage` to bytes using `IBufferWriter<byte>`
- `Http10Decoder` — stateful parser; maintains `_remainder` for incomplete responses across TCP reads

**Test coverage:** 17 files, 233 tests (RFC 1945 §1–14).

## HTTP/1.1 — RFC 9112

HTTP/1.1 adds persistent connections, chunked transfer encoding, and pipelining support.

**Supported features:**

- Persistent connections (`Connection: keep-alive` by default, `Connection: close` to opt out)
- Chunked transfer encoding — streaming request and response bodies
- `Content-Length` and `Transfer-Encoding` body framing
- Request pipelining via `CorrelationHttp1XStage` (FIFO matching)
- `Expect: 100-continue` handling
- Trailer headers in chunked responses
- `ConnectionReuseEvaluator` — RFC 9112 §9 keep-alive/close decision

**Connection management:**

The `HostPoolActor` maintains a pool of connections per host. Connections are reused when the server signals willingness (`Connection: keep-alive`) and `ConnectionReuseEvaluator` confirms the response is complete. Idle connections are evicted after a configurable timeout.

**Test coverage:** 26 files, 374 tests (RFC 9112 §1–9).

## HTTP/2 — RFC 9113 + RFC 7541

HTTP/2 multiplexes multiple streams over a single TCP connection, using binary framing and HPACK header compression.

### Frame Types

All 10 HTTP/2 frame types are implemented:

| Frame | Type | Description |
|-------|------|-------------|
| `DATA` | 0x0 | Request/response body |
| `HEADERS` | 0x1 | Header block fragment |
| `PRIORITY` | 0x2 | Stream dependency (deprecated in RFC 9113) |
| `RST_STREAM` | 0x3 | Stream termination |
| `SETTINGS` | 0x4 | Connection parameters |
| `PUSH_PROMISE` | 0x5 | Server push initiation |
| `PING` | 0x6 | Connection keepalive |
| `GOAWAY` | 0x7 | Connection shutdown |
| `WINDOW_UPDATE` | 0x8 | Flow control credit |
| `CONTINUATION` | 0x9 | Header block continuation |

### HPACK — RFC 7541

HPACK compresses headers using a combination of static table lookups, dynamic table entries, and Huffman coding.

- **Static table** — 61 pre-defined header/value pairs
- **Dynamic table** — per-connection FIFO with configurable max size (default 4096 bytes, 32-byte per-entry overhead)
- **Huffman coding** — reduces header size by ~25% on average
- **Sensitive headers** — `Authorization` and `Cookie` use `NeverIndex` to prevent dynamic table storage

### Connection Preface

The `PrependPrefaceStage` injects the HTTP/2 connection preface (`PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n`) on first connect, followed by a `SETTINGS` frame.

### Flow Control

HTTP/2 uses a credit-based flow control scheme:

- **Connection-level** — total bytes in flight across all streams
- **Stream-level** — per-stream byte budget
- `Http20ConnectionStage` tracks both levels and sends `WINDOW_UPDATE` frames automatically

### Stream Management

- `StreamIdAllocatorStage` — allocates client-initiated stream IDs (1, 3, 5, …)
- `Http20StreamStage` — assembles `HEADERS` + `CONTINUATION` + `DATA` frames into `HttpResponseMessage`
- `CorrelationHttp20Stage` — matches responses to requests by stream ID

**Test coverage:** 27 files, 545 tests (RFC 9113 §1–10; RFC 7541 §1–6).

## Protocol Selection

TurboHttp selects the protocol based on `HttpRequestMessage.Version`:

| `Version` | Protocol |
|-----------|---------|
| `HttpVersion.Version10` | HTTP/1.0 |
| `HttpVersion.Version11` | HTTP/1.1 |
| `HttpVersion.Version20` | HTTP/2 |

Set `DefaultRequestVersion` on the client options to choose a default for all requests. Individual requests can override this per-message.

## RFC Compliance Summary

| RFC | Subject | Tests |
|-----|---------|-------|
| RFC 1945 | HTTP/1.0 | 233 |
| RFC 9112 | HTTP/1.1 message framing + connection management | 374 |
| RFC 9113 | HTTP/2 | 545 |
| RFC 7541 | HPACK header compression | 419 |
| RFC 9110 | HTTP semantics (redirects, retries) | 123 |
| RFC 9111 | HTTP caching | 75 |
| RFC 6265 | Cookies | 66 |
| **Total** | | **1,835** |
