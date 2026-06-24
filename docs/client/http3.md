# HTTP/3 & QUIC

HTTP/3 runs over QUIC instead of TCP, eliminating head-of-line blocking at the transport layer and enabling features like connection migration and 0-RTT handshakes.

## When to Use HTTP/3

HTTP/3 is beneficial when:

- **Latency matters** — QUIC combines the TLS and transport handshakes, reducing connection setup from 2-3 round trips (TCP + TLS) to 1 (or 0 with 0-RTT).
- **Mobile or unstable networks** — connection migration keeps the session alive when the client's IP address changes (e.g., Wi-Fi to cellular).
- **High packet loss** — unlike TCP, QUIC streams are independent at the transport layer. A lost packet on one stream doesn't block other streams.

If your server doesn't support HTTP/3, or you're on a stable, low-latency network, HTTP/2 is equally effective.

## Enabling HTTP/3

Set `DefaultRequestVersion` on the client after obtaining it from the factory:

```csharp
builder.Services.AddGaudiHttpClient("http3-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
});

// ...

var client = factory.CreateClient("http3-api");
client.DefaultRequestVersion = HttpVersion.Version30;
```

To allow graceful fallback to HTTP/2 or HTTP/1.1 when the server doesn't support HTTP/3:

```csharp
client.DefaultRequestVersion = HttpVersion.Version30;
client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
```

## Configuration

HTTP/3 options are configured on the nested `Http3` sub-object of `TurboClientOptions`:

```csharp
builder.Services.AddGaudiHttpClient("http3-api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");

    options.Http3.MaxConnectionsPerServer = 4;    // default: 4
    options.Http3.IdleTimeout = TimeSpan.FromSeconds(60);  // default: 30 s
    options.Http3.MaxReconnectAttempts = 5;        // default: 3
});
```

### All HTTP/3 options

| Property                   | Type       | Default              | Description                                               |
| -------------------------- | ---------- | -------------------- | --------------------------------------------------------- |
| `MaxConnectionsPerServer`  | `int`      | `4`                  | Max concurrent QUIC connections per host                  |
| `MaxConcurrentStreams`      | `int`      | `100`                | Max concurrent request streams per connection             |
| `QpackMaxTableCapacity`    | `int`      | `16 * 1024` (16 KiB) | QPACK dynamic table size in bytes                         |
| `QpackBlockedStreams`      | `int`      | `100`                | Max streams blocked waiting for QPACK encoder             |
| `MaxFieldSectionSize`      | `int`      | `64 * 1024` (64 KiB) | Max header block size                                     |
| `IdleTimeout`              | `TimeSpan` | `30 s`               | QUIC idle timeout                                         |
| `MaxReconnectAttempts`     | `int`      | `3`                  | Max reconnect attempts on connection drop                 |
| `EnableAltSvcDiscovery`    | `bool`     | `false`              | Auto-discover HTTP/3 via Alt-Svc headers                  |
| `MaxReconnectBufferSize`   | `int`      | `64`                 | Max number of requests buffered during a reconnect        |

## Alt-Svc Discovery

GaudiHTTP can automatically discover HTTP/3 availability by reading `Alt-Svc` headers from HTTP/1.1 and HTTP/2 responses. When a server advertises `h3` support, subsequent requests to that host are upgraded to HTTP/3:

```csharp
options.Http3.EnableAltSvcDiscovery = true;  // default: false
```

This is opt-in because not all environments support QUIC (firewalls may block UDP). Enable it when you know your network path supports QUIC and want automatic protocol upgrade.

## HTTP/3 and Forward Proxies

QUIC cannot traverse an HTTP forward proxy (`CONNECT` tunnels carry TCP, not UDP). When a proxy is configured and applies to a request:

- HTTP/3 requests with `HttpVersionPolicy.RequestVersionOrLower` (the default) are transparently downgraded to HTTP/2 and tunneled via `CONNECT`.
- HTTP/3 requests with `RequestVersionExact` or `RequestVersionOrHigher` fail with `HttpRequestException`.
- Alt-Svc HTTP/3 upgrades are skipped for proxied hosts.

Hosts matched by the proxy's bypass list keep using HTTP/3 directly.

## QPACK Header Compression

HTTP/3 uses QPACK for header compression (the QUIC equivalent of HPACK in HTTP/2). GaudiHTTP manages QPACK encoding and decoding automatically. Tune the dynamic table size if needed:

```csharp
options.Http3.QpackMaxTableCapacity = 8192;  // default: 16 * 1024 (16 KiB)
options.Http3.QpackBlockedStreams = 200;      // default: 100
```

Larger tables improve compression ratio for APIs with many repeated headers but use more memory per connection.

::: info How it works
See [Architecture: Protocol Engines](/architecture/engines) for details on how protocol negotiation and engine selection work.
:::
