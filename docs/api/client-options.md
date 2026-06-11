# TurboClientOptions

```csharp
public sealed class TurboClientOptions
{
    // Base address
    public Uri? BaseAddress { get; set; }

    // Version-specific options (nested)
    public Http1ClientOptions Http1 { get; init; } = new();    // HTTP/1.x settings
    public Http2ClientOptions Http2 { get; init; } = new();    // HTTP/2 settings
    public Http3ClientOptions Http3 { get; init; } = new();    // HTTP/3 settings

    // Body buffering (response buffering threshold lives on Http1.MaxBufferedResponseBodySize)
    public long? MaxStreamedResponseBodySize { get; set; }             // null = unlimited; cap on a streamed response body
    public int RequestBodyChunkSize { get; set; } = 16 * 1024;         // 16 KB; chunk size when streaming a request body

    // Connection pool
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan PooledConnectionIdleTimeout { get; set; } = TimeSpan.FromSeconds(90);
    public TimeSpan PooledConnectionLifetime { get; set; } = Timeout.InfiniteTimeSpan;
    public uint MaxConcurrentEndpoints { get; set; } = 256;

    // TLS
    public bool DangerousAcceptAnyServerCertificate { get; set; }
    public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; set; }
    public X509CertificateCollection? ClientCertificates { get; set; }
    public SslProtocols EnabledSslProtocols { get; set; } = SslProtocols.None;

    // Socket and buffer options
    public int? SocketSendBufferSize { get; set; }
    public int? SocketReceiveBufferSize { get; set; }
    public int ReceiveBufferHint { get; set; } = 64 * 1024;     // 64 KB; internal receive buffer size hint
    public int MinimumSegmentSize { get; set; } = 16 * 1024;    // 16 KB; minimum segment size of the internal buffer pool

    // Proxy
    public bool UseProxy { get; set; } = true;
    public IWebProxy? Proxy { get; set; }
    public ICredentials? DefaultProxyCredentials { get; set; }

    // Authentication
    public ICredentials? Credentials { get; set; }
    public bool PreAuthenticate { get; set; }
}
```

## Connection Options

| Property | Default | Description |
|----------|---------|-------------|
| `BaseAddress` | `null` | Base URI for relative requests |
| `ConnectTimeout` | `15 s` | TCP/QUIC connection timeout |
| `PooledConnectionIdleTimeout` | `90 s` | How long idle connections are kept in the pool |
| `PooledConnectionLifetime` | `infinite` | Maximum lifetime of a pooled connection |
| `MaxConcurrentEndpoints` | `256` | Max concurrently active endpoints |

Per-version connection limits are configured on the nested options objects:

| Property | Default | Description |
|----------|---------|-------------|
| `Http1.MaxConnectionsPerServer` | `6` | Max concurrent HTTP/1.x connections per host |
| `Http1.MaxPipelineDepth` | `16` | Max pipelined requests per HTTP/1.1 connection |
| `Http2.MaxConnectionsPerServer` | `6` | Max concurrent HTTP/2 connections per host |
| `Http2.MaxConcurrentStreams` | `100` | Max concurrent streams per HTTP/2 connection |
| `Http3.MaxConnectionsPerServer` | `4` | Max concurrent QUIC connections per host |

See [Connection Pooling guide](/client/connection-pooling) for pool lifecycle details.

## HTTP/1.x Options

```csharp
public sealed class Http1ClientOptions
{
    public int MaxBufferedResponseBodySize { get; set; } = 64 * 1024;   // 64 KB; bodies up to this size are buffered in memory, larger are streamed
    public int MaxConnectionsPerServer { get; set; } = 6;
    public int MaxPipelineDepth { get; set; } = 16;
    public int MaxResponseHeadersLength { get; set; } = 64;             // KB
    public bool AutoHost { get; set; } = true;
    public bool AutoAcceptEncoding { get; set; } = true;
    public int MaxReconnectAttempts { get; set; } = 3;
    public int MaxResponseHeaderCount { get; set; } = 100;              // max number of response header fields
    public int MaxResponseHeaderLineLength { get; set; } = 8 * 1024;   // 8 KB; max length of a single header line
    public int MaxChunkExtensionLength { get; set; } = int.MaxValue;   // max total length of chunk extensions; unbounded by default
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MaxBufferedResponseBodySize` | `64 * 1024` (64 KB) | Response bodies up to this size are buffered fully in memory; larger bodies are exposed as a streaming pipe |
| `MaxConnectionsPerServer` | `6` | Max concurrent TCP connections per host |
| `MaxPipelineDepth` | `16` | Max pipelined requests per connection |
| `MaxResponseHeadersLength` | `64` (KB) | Max total response header block size |
| `AutoHost` | `true` | Automatically inject `Host` header |
| `AutoAcceptEncoding` | `true` | Automatically inject `Accept-Encoding` header |
| `MaxReconnectAttempts` | `3` | Max reconnect attempts on connection drop |
| `MaxResponseHeaderCount` | `100` | Max number of response header fields |
| `MaxResponseHeaderLineLength` | `8 * 1024` (8 KB) | Max length of a single response header line |
| `MaxChunkExtensionLength` | `int.MaxValue` | Max total length of chunk extensions; unbounded by default |

## HTTP/2 Options

```csharp
public sealed class Http2ClientOptions
{
    public int MaxConnectionsPerServer { get; set; } = 6;
    public int MaxConcurrentStreams { get; set; } = 100;
    public int InitialConnectionWindowSize { get; set; } = 64 * 1024 * 1024;  // 64 MB
    public int InitialStreamWindowSize { get; set; } = 1 * 1024 * 1024;       // 1 MB
    public int MaxStreamWindowSize { get; set; } = 16 * 1024 * 1024;          // 16 MB
    public double WindowScaleThresholdMultiplier { get; set; } = 1.0;
    public bool EnableAdaptiveWindowScaling { get; set; } = true;
    public int MaxFrameSize { get; set; } = 64 * 1024;                        // 64 KB
    public int HeaderTableSize { get; set; } = 64 * 1024;                     // 64 KB
    public int MaxResponseHeaderListSize { get; set; } = 64 * 1024;           // 64 KB; max total size of response header list
    public long MaxBufferedRequestBodySize { get; set; } = 64 * 1024;         // 64 KB; bodies up to this size are serialized inline, larger are streamed
    public long MaxRequestBodyBufferSize { get; set; } = 64 * 1024;           // 64 KB; outbound body bytes buffered per stream before the encoder pauses
    public int MaxReconnectAttempts { get; set; } = 3;
    public int MaxReconnectBufferSize { get; set; } = 64;                     // max requests buffered during reconnection
    public TimeSpan KeepAlivePingDelay { get; set; } = Timeout.InfiniteTimeSpan;
    public TimeSpan KeepAlivePingTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public HttpKeepAlivePingPolicy KeepAlivePingPolicy { get; set; } = HttpKeepAlivePingPolicy.Always;
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MaxConnectionsPerServer` | `6` | Max concurrent TCP connections per host |
| `MaxConcurrentStreams` | `100` | Max concurrent streams per connection |
| `InitialConnectionWindowSize` | `64 * 1024 * 1024` (64 MB) | Connection-level flow control window |
| `InitialStreamWindowSize` | `1 * 1024 * 1024` (1 MB) | Initial per-stream flow control window; grows up to `MaxStreamWindowSize` under adaptive scaling |
| `MaxStreamWindowSize` | `16 * 1024 * 1024` (16 MB) | Maximum per-stream flow control window |
| `WindowScaleThresholdMultiplier` | `1.0` | RTT multiplier controlling when to scale the stream window |
| `EnableAdaptiveWindowScaling` | `true` | Grow the stream receive window based on observed throughput |
| `MaxFrameSize` | `64 * 1024` (64 KB) | Max frame payload size |
| `HeaderTableSize` | `64 * 1024` (64 KB) | HPACK dynamic table size |
| `MaxResponseHeaderListSize` | `64 * 1024` (64 KB) | Max total size of the response header list |
| `MaxBufferedRequestBodySize` | `64 * 1024` (64 KB) | Request bodies up to this size are serialized inline; larger bodies are streamed in chunks with backpressure |
| `MaxRequestBodyBufferSize` | `64 * 1024` (64 KB) | Max outbound body bytes buffered per stream before the body encoder pauses |
| `MaxReconnectAttempts` | `3` | Max reconnect attempts on connection drop |
| `MaxReconnectBufferSize` | `64` | Max requests buffered during reconnection |
| `KeepAlivePingDelay` | `infinite` | Delay before sending keep-alive PING |
| `KeepAlivePingTimeout` | `20 s` | Timeout for PING acknowledgment |
| `KeepAlivePingPolicy` | `Always` | When to send keep-alive PINGs |

### Adjusting Frame Size

```csharp
// Increase frame size for large binary payloads (default: 64 KiB, max: 16 MiB)
options.Http2.MaxFrameSize = 4 * 1024 * 1024; // 4 MiB
```

See [HTTP/2 & Multiplexing guide](/client/http2) for multiplexing configuration.

## HTTP/3 Options

```csharp
public sealed class Http3ClientOptions
{
    public int MaxConnectionsPerServer { get; set; } = 4;
    public int MaxConcurrentStreams { get; set; } = 100;
    public int QpackMaxTableCapacity { get; set; } = 16 * 1024;  // 16 KB
    public int QpackBlockedStreams { get; set; } = 100;
    public int MaxFieldSectionSize { get; set; } = 64 * 1024;    // 64 KB
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxReconnectAttempts { get; set; } = 3;
    public bool EnableAltSvcDiscovery { get; set; }
    public int MaxReconnectBufferSize { get; set; } = 64;
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `MaxConnectionsPerServer` | `4` | Max concurrent QUIC connections per host |
| `MaxConcurrentStreams` | `100` | Max concurrent streams per connection |
| `QpackMaxTableCapacity` | `16 * 1024` (16 KB) | QPACK dynamic table size |
| `QpackBlockedStreams` | `100` | Max streams blocked waiting for QPACK |
| `MaxFieldSectionSize` | `64 * 1024` (64 KB) | Max header block size |
| `IdleTimeout` | `30 s` | QUIC idle timeout |
| `MaxReconnectAttempts` | `3` | Max reconnect attempts on connection drop |
| `EnableAltSvcDiscovery` | `false` | Auto-discover HTTP/3 via Alt-Svc headers |
| `MaxReconnectBufferSize` | `64` | Max datagram buffers during reconnection |

See [HTTP/3 & QUIC guide](/client/http3) for QUIC-specific settings.

## TLS Options

| Property | Default | Description |
|----------|---------|-------------|
| `DangerousAcceptAnyServerCertificate` | `false` | Skip all certificate validation — dev/test only |
| `ServerCertificateValidationCallback` | Accept valid certs | Custom TLS certificate validation |
| `ClientCertificates` | `null` | Client certificates for mutual TLS |
| `EnabledSslProtocols` | `SslProtocols.None` (OS default) | TLS protocol versions to permit |

```csharp
// Mutual TLS with a client certificate
options.ClientCertificates = new X509CertificateCollection
{
    X509CertificateLoader.LoadPkcs12FromFile("client.pfx", password)
};
```

## Socket Options

| Property | Default | Description |
|----------|---------|-------------|
| `SocketSendBufferSize` | `null` (system default) | OS socket send buffer size in bytes |
| `SocketReceiveBufferSize` | `null` (system default) | OS socket receive buffer size in bytes |
| `ReceiveBufferHint` | `64 * 1024` (64 KB) | Size hint for the internal receive buffer; larger values reduce read syscalls at the cost of memory |
| `MinimumSegmentSize` | `16 * 1024` (16 KB) | Minimum segment size of the internal buffer pool |

## Proxy Options

| Property | Default | Description |
|----------|---------|-------------|
| `UseProxy` | `true` | Use system proxy settings |
| `Proxy` | `null` | Custom proxy URI |
| `DefaultProxyCredentials` | `null` | Credentials for proxy authentication |

## Authentication Options

| Property | Default | Description |
|----------|---------|-------------|
| `Credentials` | `null` | Credentials for HTTP authentication (Basic, Digest, etc.) |
| `PreAuthenticate` | `false` | Send credentials proactively before receiving a challenge |

## Body Buffering Options

| Property | Default | Description |
|----------|---------|-------------|
| `Http1.MaxBufferedResponseBodySize` | `64 * 1024` (64 KB) | HTTP/1.x response bodies up to this size are buffered fully in memory; larger bodies are exposed as a streaming pipe |
| `MaxStreamedResponseBodySize` | `null` (unlimited) | Cap on a streamed response body; `null` means no limit |
| `RequestBodyChunkSize` | `16 * 1024` (16 KB) | Chunk size used when streaming a request body |

::: tip
For large file downloads or uploads, consume the response as a stream. `MaxStreamedResponseBodySize` defaults to `null` — there is no built-in size cap on streamed responses.
:::
