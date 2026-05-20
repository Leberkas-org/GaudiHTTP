# Server Protocol Engines

Each server engine is responsible for decoding incoming request bytes and encoding response bytes for a specific HTTP version. The `NegotiatingServerEngine` selects the appropriate version-specific engine using `ProtocolRouter`, based on ALPN negotiation (TLS) or auto-detection (plaintext).

---

## Protocol Negotiation

When a connection arrives, `NegotiatingServerEngine` delegates to `ProtocolRouter` to inspect the connection and select the correct engine:

| Protocol | Detection Method                                          | Server Engine                |
| -------- | --------------------------------------------------------- | ---------------------------- |
| HTTP/1.0 | First line is `METHOD /path HTTP/1.0` (ASCII)             | `Http10ServerEngine`         |
| HTTP/1.1 | First line is `METHOD /path HTTP/1.1` (ASCII)             | `Http11ServerEngine`         |
| HTTP/2   | First bytes are `PRI * HTTP/2.0` preface or `SETTINGS`    | `Http20ServerEngine`         |
| HTTP/3   | Connection over QUIC (UDP); ALPN or auto-detect           | `Http30ServerEngine`         |

With TLS, ALPN negotiation happens during the TLS handshake. The client sends advertised protocols (`h2`, `h3`, `http/1.1`, `http/1.0`), and the server selects one. Without TLS, the router auto-detects from the first bytes received.

---

## HTTP/1.0 Server Engine

<ClientOnly>
  <LikeC4Diagram viewId="serverHttp10Engine" :height="300" />
</ClientOnly>

`Http10ServerEngine` handles the simplest protocol: one request per connection, then close.

**Characteristics:**
- Each connection handles exactly one request
- After sending the response, the connection closes
- No keep-alive, no pipelining
- Response body length determined by `Content-Length` header

**Transport:**
- `TcpListenerFactory` — TCP listener binds to configured port
- `TcpConnectionStage` — transport reads/writes bytes

---

## HTTP/1.1 Server Engine

<ClientOnly>
  <LikeC4Diagram viewId="serverHttp11Engine" :height="340" />
</ClientOnly>

`Http11ServerEngine` adds **persistent connections** and **keep-alive control**.

**Characteristics:**
- Connections persist after each response (`Connection: keep-alive`)
- Supports pipelining — multiple requests queued for sequential processing
- Chunked transfer encoding for streaming responses
- Keep-alive timeout configurable via `TurboServerOptions.Http1.IdleTimeout`

**Transport:**
- `TcpListenerFactory` — TCP listener binds to configured port
- `TcpConnectionStage` — transport reuses connections across multiple requests

**Keep-Alive Handling:**

After encoding each response, `Http11ServerEngine` evaluates the `Connection` header:

- `Connection: keep-alive` (or HTTP/1.1 default) → the connection remains open for the next request
- `Connection: close` → the connection closes after sending the response
- Idle timeout → if no new request arrives within the idle timeout, the connection closes

---

## HTTP/2 Server Engine

<ClientOnly>
  <LikeC4Diagram viewId="serverHttp2Engine" :height="380" />
</ClientOnly>

`Http20ServerEngine` provides **stream multiplexing** — many logical requests share a single TCP connection.

**Characteristics:**
- Single TCP connection carries multiple concurrent streams
- Each stream has a unique stream ID allocated by the client
- HPACK header compression with synchronized dynamic tables
- Connection-level and stream-level flow control
- Server push support

**Transport:**
- `TcpListenerFactory` — TCP listener binds to configured port
- ALPN negotiation — client advertises `h2`; server selects it

**Internal Features:**

- **Frame parsing** — decodes 9-byte frame headers + payloads
- **HPACK decoding** — decompresses request headers using the dynamic table
- **Flow control** — tracks connection-level and per-stream receive windows; applies backpressure
- **Connection frames** — `SETTINGS`, `PING`, `GOAWAY`, `WINDOW_UPDATE`
- **Stream correlation** — assembles `HEADERS` + `DATA` frames per stream into `HttpRequestMessage`

**Configuration:**

```csharp
var options = new TurboServerOptions
{
    Http2 = new Http2Options
    {
        MaxFrameSize = 16 * 1024,           // default 16KB
        MaxHeaderListSize = 32 * 1024,      // default 32KB
        InitialWindowSize = 65_535,         // stream-level flow control window
        InitialConnectionWindowSize = 1 * 1024 * 1024, // connection-level window
    }
};
```

---

## HTTP/3 Server Engine

<ClientOnly>
  <LikeC4Diagram viewId="serverHttp3Engine" :height="380" />
</ClientOnly>

`Http30ServerEngine` runs over **QUIC** (UDP-based transport), eliminating head-of-line blocking.

**Characteristics:**
- QUIC (UDP) replaces TCP, providing built-in encryption (TLS)
- Each request uses its own QUIC stream — lost packets don't block other streams
- QPACK header compression (adapted for out-of-order delivery)
- Connection-level flow control only (stream flow control handled by QUIC)

**Transport:**
- `QuicListenerFactory` — QUIC listener binds to configured port (UDP)
- ALPN negotiation — client advertises `h3`; server selects it

**Internal Features:**

- **Frame parsing** — uses QUIC variable-length integer encoding
- **QPACK decoding** — decompresses request headers with decoder instruction streams
- **Stream management** — per-stream lifecycle independent of other streams
- **Connection frames** — `SETTINGS`, `GOAWAY`

**Configuration:**

```csharp
var options = new TurboServerOptions
{
    Http3 = new Http3Options
    {
        MaxFrameSize = 16 * 1024,           // default 16KB
        MaxHeaderListSize = 32 * 1024,      // default 32KB
        InitialMaxStreamDataBidiLocal = 1 * 1024 * 1024,   // per-stream flow control
        InitialMaxData = 10 * 1024 * 1024,  // connection-level flow control
    }
};
```

---

## Per-Protocol Configuration

Each protocol has its own configuration section on `TurboServerOptions`:

```csharp
var options = new TurboServerOptions
{
    Binding = new BindingOptions
    {
        Port = 8080,
        EnableHttp1 = true,
        EnableHttp2 = true,
        EnableHttp3 = true,
    },
    Http1 = new Http1Options { IdleTimeout = TimeSpan.FromSeconds(120) },
    Http2 = new Http2Options { MaxFrameSize = 32 * 1024 },
    Http3 = new Http3Options { MaxHeaderListSize = 64 * 1024 },
};
```

::: tip
For most applications, the default configuration works well. Only adjust these settings if you have specific protocol requirements or are tuning for a particular workload.
:::

---

## ALPN Negotiation (TLS)

When TLS is enabled, the client advertises supported protocols during the handshake:

```
Client TLS ClientHello
    ↓
    application_layer_protocol_negotiation (ALPN)
    supported_protocols: ["h3", "h2", "http/1.1"]
    ↓
Server TLS ServerHello
    ↓
    application_layer_protocol_negotiation (ALPN)
    selected_protocol: "h2"
    ↓
Client & Server both use HTTP/2
```

If no ALPN is advertised or negotiation fails, the server defaults to **HTTP/1.1**.

---

## Related Guides

- [HTTP/2 & Multiplexing](/client/http2) — client-side HTTP/2 configuration
- [HTTP/3 & QUIC](/client/http3) — client-side HTTP/3 configuration
- [Server Configuration](/server/configuration) — server protocol settings
- [Hosting & Lifecycle](/server/hosting) — connection management
