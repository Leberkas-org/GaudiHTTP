# Configuration

All server configuration flows through `GaudiServerOptions`, passed to `UseGaudiHttp()`.

```csharp
builder.Host.UseGaudiHttp(options =>
{
    // configure here
});
```

## General Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `HandlerTimeout` | `TimeSpan` | 30s | Maximum time for a request handler to complete |
| `HandlerGracePeriod` | `TimeSpan` | 5s | Extra time after handler timeout before force-closing |
| `GracefulShutdownTimeout` | `TimeSpan` | 30s | Time to drain connections during shutdown |
| `BodyConsumptionTimeout` | `TimeSpan` | 30s | Time for the app to consume the request body |
| `ResponseBodyChunkSize` | `int` | 16 * 1024 | Chunk size for response body writes |
| `MaxOutboundCoalesceCount` | `int` | 32 | Coalesce factor for outbound writes — frames are merged up to factor × 16 KiB per transport write |
| `AllowResponseHeaderCompression` | `bool` | true | Whether response headers may use Huffman compression (HPACK/QPACK); disable to mitigate CRIME/BREACH-style attacks |

## Connection Limits

Access via `options.Limits`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxConcurrentConnections` | `int` | 0 (unlimited) | Maximum concurrent connections |
| `MaxRequestBodySize` | `long` | 30,000,000 (~28.6 MiB) | Global max request body size (matches Kestrel) |
| `MaxResponseBufferSize` | `long` | 64 * 1024 | Maximum per-stream response write buffer |
| `MaxRequestBufferSize` | `long?` | 1 MiB | Transport input buffer before backpressure is applied (`null` = unlimited) |
| `MaxRequestHeaderCount` | `int` | 100 | Maximum request headers |
| `MaxRequestHeadersTotalSize` | `int` | 32 * 1024 | Maximum total header bytes |
| `MaxResetStreamsPerWindow` | `int` | 200 | Maximum HTTP/2 stream resets tolerated in a sliding window before the connection is closed (Rapid Reset / CVE-2023-44487 mitigation). Set to 0 to disable. |
| `KeepAliveTimeout` | `TimeSpan` | 130s | Idle connection timeout |
| `RequestHeadersTimeout` | `TimeSpan` | 30s | Time to receive request headers |
| `MinRequestBodyDataRate` | `double` | 240 | Minimum body bytes/sec (0 = disabled) |
| `MinRequestBodyDataRateGracePeriod` | `TimeSpan` | 5s | Grace period before enforcing body rate |
| `MinResponseDataRate` | `double` | 240 | Minimum response bytes/sec (0 = disabled) |
| `MinResponseDataRateGracePeriod` | `TimeSpan` | 5s | Grace period before enforcing response rate |

## HTTP/1.x Options

Access via `options.Http1`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxRequestLineLength` | `int` | 8192 | Maximum bytes for the request line |
| `MaxRequestTargetLength` | `int` | 8192 | Maximum bytes for the request target (URL) |
| `MaxPipelinedRequests` | `int` | 16 | Maximum queued pipelined requests |
| `MaxChunkExtensionLength` | `int` | 4096 | Maximum bytes for chunk extensions |
| `MaxBufferedRequestBodySize` | `int` | 64 * 1024 | Request bodies up to this size are buffered fully in memory; larger bodies are exposed as a streaming pipe |
| `BodyReadTimeout` | `TimeSpan` | 30s | Timeout for reading request body |
| `MaxHeaderListSize` | `int?` | null (uses global) | Max total header bytes (null = uses `Limits.MaxRequestHeadersTotalSize`) |
| `MaxRequestBodySize` | `long?` | null (uses global) | HTTP/1.x-specific body size limit |
| `KeepAliveTimeout` | `TimeSpan?` | null (uses global) | Per-protocol keep-alive override |
| `RequestHeadersTimeout` | `TimeSpan?` | null (uses global) | Per-protocol headers timeout override |
| `MinRequestBodyDataRate` | `double?` | null (uses global) | Per-protocol minimum body bytes/sec override |
| `MinRequestBodyDataRateGracePeriod` | `TimeSpan?` | null (uses global) | Grace period before enforcing body rate |
| `MinResponseDataRate` | `double?` | null (uses global) | Per-protocol minimum response bytes/sec override |
| `MinResponseDataRateGracePeriod` | `TimeSpan?` | null (uses global) | Grace period before enforcing response rate |

## HTTP/2 Options

Access via `options.Http2`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxConcurrentStreams` | `int` | 100 | Maximum concurrent streams per connection |
| `InitialConnectionWindowSize` | `int` | 1 * 1024 * 1024 | Connection-level flow control window |
| `InitialStreamWindowSize` | `int` | 768 * 1024 | Per-stream flow control window (starting point for adaptive scaling) |
| `MaxStreamWindowSize` | `int` | 8 * 1024 * 1024 | Upper bound for adaptive per-stream window growth |
| `WindowScaleThresholdMultiplier` | `double` | 1.0 | Threshold multiplier for adaptive window growth; higher values grow less eagerly |
| `EnableAdaptiveWindowScaling` | `bool` | true | Grow the per-stream receive window based on measured throughput and RTT |
| `MaxFrameSize` | `int` | 16 * 1024 | Maximum HTTP/2 frame payload size |
| `MaxHeaderListSize` | `int?` | null (uses global) | Max total header bytes (null = uses `Limits.MaxRequestHeadersTotalSize`) |
| `HeaderTableSize` | `int` | 4 * 1024 | HPACK dynamic table size |
| `MaxResponseBufferSize` | `long?` | null (uses global) | Response buffering before backpressure (null = uses `Limits.MaxResponseBufferSize`) |
| `MaxRequestBodySize` | `long?` | null (uses global) | HTTP/2-specific body size limit |
| `KeepAliveTimeout` | `TimeSpan?` | null (uses global) | Connection idle timeout |
| `KeepAlivePingDelay` | `TimeSpan` | infinite (disabled) | Idle time after the last received frame before the server sends a keep-alive PING |
| `KeepAlivePingTimeout` | `TimeSpan` | 20s | Max wait for a PING ACK before the connection is closed |
| `RequestHeadersTimeout` | `TimeSpan?` | null (uses global) | Time to receive request headers |
| `MinRequestBodyDataRate` | `double?` | null (uses global) | Minimum body bytes/sec |
| `MinRequestBodyDataRateGracePeriod` | `TimeSpan?` | null (uses global) | Grace period before enforcing body rate |
| `MinResponseDataRate` | `double?` | null (uses global) | Minimum response bytes/sec |
| `MinResponseDataRateGracePeriod` | `TimeSpan?` | null (uses global) | Grace period before enforcing response rate |

## HTTP/3 Options

Access via `options.Http3`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxConcurrentStreams` | `int` | 100 | Maximum concurrent streams per connection |
| `MaxHeaderListSize` | `int?` | null (uses global) | Max total header bytes (null = uses `Limits.MaxRequestHeadersTotalSize`) |
| `QpackMaxTableCapacity` | `int` | 0 | QPACK dynamic table capacity (0 = static only) |
| `QpackBlockedStreams` | `int` | 100 | Maximum concurrent QPACK-blocked streams |
| `MaxResponseBufferSize` | `long?` | null (uses global) | Per-stream response write buffer (null = uses `Limits.MaxResponseBufferSize`) |
| `MaxRequestBodySize` | `long?` | null (uses global) | HTTP/3-specific body size limit |
| `KeepAliveTimeout` | `TimeSpan?` | null (uses global) | Connection idle timeout |
| `RequestHeadersTimeout` | `TimeSpan?` | null (uses global) | Time to receive request headers |
| `MinRequestBodyDataRate` | `double?` | null (uses global) | Minimum body bytes/sec |
| `MinRequestBodyDataRateGracePeriod` | `TimeSpan?` | null (uses global) | Grace period before enforcing body rate |
| `MinResponseDataRate` | `double?` | null (uses global) | Minimum response bytes/sec |
| `MinResponseDataRateGracePeriod` | `TimeSpan?` | null (uses global) | Grace period before enforcing response rate |

## Transport Buffers

Per-endpoint backpressure thresholds for the pipes between the OS socket and the HTTP pipeline. Set via `GaudiListenOptions.Transport`. All properties are nullable; each `null` property falls back to its protocol-optimized default individually (TCP buffers one pipe per connection, QUIC one pipe per stream), so you only set what you want to change. A resume threshold above its pause threshold fails endpoint resolution with `InvalidOperationException`.

| Property | Type | TCP Default | QUIC Default | Description |
|----------|------|-------------|--------------|-------------|
| `InputPauseThreshold` | `long?` | 1 MiB | 64 KiB | Bytes buffered on the read pipe before the OS socket is paused |
| `InputResumeThreshold` | `long?` | 512 KiB | 32 KiB | Buffered byte count at which reading resumes |
| `OutputPauseThreshold` | `long?` | 64 KiB | 64 KiB | Bytes buffered on the write pipe before the HTTP pipeline is paused |
| `OutputResumeThreshold` | `long?` | 32 KiB | 32 KiB | Buffered byte count at which writing resumes |
| `MinimumSegmentSize` | `int?` | 16 KiB | 4 KiB | Minimum pipe buffer segment size |
| `ReceiveBufferHint` | `int?` | 64 KiB | 4 KiB | Size hint for `PipeWriter.GetMemory` on the receive path |

```csharp
options.Listen(IPAddress.Any, 8080, listen =>
{
    // Only the input thresholds are overridden; everything else keeps the TCP defaults
    listen.Transport = new TransportBufferOptions
    {
        InputPauseThreshold = 2 * 1024 * 1024,
        InputResumeThreshold = 1024 * 1024
    };
});
```

See the [Server API reference](/api/server#transport-buffer-options) for details.

## Example: Full Configuration

```csharp
builder.Host.UseGaudiHttp(options =>
{
    // Endpoints
    options.ListenLocalhost(5000);
    options.ListenLocalhost(5001, listen =>
    {
        listen.UseHttps();
        listen.Protocols = HttpProtocols.Http1AndHttp2;
    });

    // Timeouts
    options.HandlerTimeout = TimeSpan.FromSeconds(60);
    options.HandlerGracePeriod = TimeSpan.FromSeconds(10);
    options.GracefulShutdownTimeout = TimeSpan.FromSeconds(30);

    // Limits
    options.Limits.MaxConcurrentConnections = 1000;
    options.Limits.MaxRequestBodySize = 50 * 1024 * 1024;

    // HTTP/2
    options.Http2.MaxConcurrentStreams = 200;
    options.Http2.InitialConnectionWindowSize = 2 * 1024 * 1024;

    // HTTP/3
    options.Http3.MaxConcurrentStreams = 200;
});
```

## Next Steps

- [Using with ASP.NET Core](./aspnet-core) — how GaudiHTTP integrates with ASP.NET Core
- [Performance Tuning](./performance) — when and how to tune these options
- [Hosting & Lifecycle](./hosting) — shutdown behavior
