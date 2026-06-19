# Server Request Pipeline

The server request pipeline shows how an incoming request flows through the server — from raw network bytes through protocol decoding to the ASP.NET Core application layer, where middleware, routing, and handlers run.

<ClientOnly>
  <LikeC4Diagram viewId="serverPipeline" :height="500" />
</ClientOnly>

---

## Request Flow

Each connection is handled by a `ConnectionActor` that owns the Akka.Streams sub-graph for that connection:

```
Incoming TCP/QUIC Connection
    ↓
[Transport] — TCP or QUIC listener accepts connection (Servus.Akka)
    ↓
[ListenerActor] — spawns ConnectionActor per client connection
    ↓
[ProtocolRouter] — picks engine by transport (QUIC → Http30ServerEngine; TCP → NegotiatingServerEngine)
    ↓
[Http*ServerEngine] — protocol-specific decoder (Http10/11/20/30ServerEngine)
    ↓
[ApplicationBridgeStage] — wraps parsed request as IFeatureCollection
    ↓
╔════════════════════════════════════════════════════════════════╗
║  ASP.NET Core takes over (Middleware → Routing → Handlers)  ║
╚════════════════════════════════════════════════════════════════╝
    ↓
[Protocol Encoder] — encodes response to wire bytes
    ↓
Outgoing TCP/QUIC Bytes
```

---

## Pipeline Stages

| Stage                        | Role                                                                                                                                      |
| ---------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| `Transport` (TCP/QUIC)       | Accepts incoming connections over TCP or QUIC (via Servus.Akka.Transport)                                                              |
| `ListenerActor`              | Binds to a port and spawns a `ConnectionActor` per incoming connection                                                                  |
| `ProtocolRouter`             | Static helper used by `ServerSupervisorActor` to pick a server engine by transport: QUIC bindings get `Http30ServerEngine` directly; TCP bindings get `NegotiatingServerEngine`, which performs byte-level protocol detection |
| `Http*ServerEngine`          | Protocol-specific state machine: parses request bytes, manages connection/stream-level flow control, encodes response frames            |
| `ApplicationBridgeStage`      | Wraps the parsed protocol request as an `IFeatureCollection` (standard ASP.NET Core `HttpContext`); then ASP.NET Core takes over    |
| **(ASP.NET Core)**           | **Middleware** (app.Use/UseMiddleware) → **Routing** (endpoint routing) → **Model Binding** → **Handler Execution** (Minimal APIs, Controllers, etc.) |

---

## Connection Lifecycle

Each connection is managed by a dedicated `ConnectionActor` and its Akka.Streams graph:

1. **Bind** — `ListenerActor` binds to a TCP or QUIC port
2. **Accept** — When a client connects, `ConnectionActor` materializes a sub-graph for that connection
3. **Materialize** — The sub-graph composes the protocol engine with `ApplicationBridgeStage` and the shared ASP.NET Core pipeline (middleware and routing)
4. **Process** — The graph processes requests and generates responses for the lifetime of the connection
5. **Cleanup** — When the client disconnects (or after idle timeout), the sub-graph completes and releases resources

---

## Response Flow

After the handler returns a response, it flows back through the pipeline:

1. ASP.NET Core populates the `IHttpResponseFeature` (status code, headers, response body stream)
2. The protocol engine encodes the response to wire bytes using the appropriate HTTP version (1.0, 1.1, 2, or 3)
3. The transport layer (via `ConnectionStage` and Servus.Akka.Transport) sends the bytes to the client
4. For HTTP/1.1+, the connection can remain open and reuse for the next request; for HTTP/1.0, the connection closes after sending the response

---

## Protocol Detection

When a new connection arrives, `ServerSupervisorActor` uses `ProtocolRouter` to pick an engine based on the transport:

- **QUIC connections** — routed directly to `Http30ServerEngine` (HTTP/3 is QUIC-only; there is no h3-over-TCP/TLS path)
- **TCP connections** — handed to `NegotiatingServerEngine`, which wraps `ProtocolNegotiatingStateMachine` to detect the protocol

`ProtocolNegotiatingStateMachine` selects the engine as follows:

- **With TLS (HTTPS)** — ALPN negotiation during the TLS handshake decides the protocol:
  - `h2` → HTTP/2
  - Any other negotiated protocol → HTTP/1.1 (fallback)
- **Without TLS (plaintext)** — the state machine buffers incoming bytes and sniffs:
  - First 4 bytes are `PRI ` → HTTP/2 (start of the HTTP/2 connection preface)
  - Request line contains `HTTP/1.0\r\n` → HTTP/1.0
  - Request line ends with `\n` → HTTP/1.1

---

## ASP.NET Core Integration

After `ApplicationBridgeStage` creates the `TContext` from the `IFeatureCollection`, ASP.NET Core's standard middleware pipeline takes over — routing, model binding, authentication, and handler execution are all handled by ASP.NET Core, not by TurboHTTP.

---

## Related Guides

- [ASP.NET Core Integration](/server/aspnet-core) — middleware, routing, and request handling
- [Hosting & Lifecycle](/server/hosting) — actor hierarchy and graceful shutdown
