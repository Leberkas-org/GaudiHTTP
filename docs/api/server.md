# Server API

GaudiHTTP Server is an `IServer` implementation for ASP.NET Core built on Akka.Streams. It replaces Kestrel as the transport layer.

## Registration

```csharp
public static class GaudiServerWebHostBuilderExtensions
{
    IHostBuilder UseGaudiHttp(
        this IHostBuilder builder,
        Action<GaudiServerOptions>? configure = null);
}
```

Register the server during application setup:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseGaudiHttp(options =>
{
    options.ListenLocalhost(5100);
});

var app = builder.Build();
app.MapGet("/", () => "Hello from GaudiHTTP!");
await app.RunAsync();
```

---

## GaudiServer

```csharp
public sealed class GaudiServer : IServer, IDisposable
{
    IFeatureCollection Features { get; }

    Task StartAsync<TContext>(
        IHttpApplication<TContext> application,
        CancellationToken cancellationToken) where TContext : notnull;

    Task StopAsync(CancellationToken cancellationToken);
}
```

`GaudiServer` implements `IServer` from `Microsoft.AspNetCore.Hosting.Server`. It creates or reuses an `ActorSystem`, materializes the Akka.Streams pipeline, and spawns the actor hierarchy for connection management.

---

## Server Options

```csharp
public sealed class GaudiServerOptions
{
    GaudiServerLimits Limits { get; }

    TimeSpan GracefulShutdownTimeout { get; set; }  // default: 30s
    TimeSpan HandlerTimeout { get; set; }            // default: 30s
    TimeSpan HandlerGracePeriod { get; set; }        // default: 5s

    TimeSpan BodyConsumptionTimeout { get; set; }    // default: 30s
    int ResponseBodyChunkSize { get; set; }          // default: 16 * 1024
    int MaxOutboundCoalesceCount { get; set; }       // default: 32 (frames merged up to factor × 16 KiB per transport write)
    bool AllowResponseHeaderCompression { get; set; } // default: true (disable to mitigate CRIME/BREACH-style attacks)

    Http1ServerOptions Http1 { get; }
    Http2ServerOptions Http2 { get; }
    Http3ServerOptions Http3 { get; }

    void Listen(IPAddress address, ushort port);
    void Listen(IPAddress address, ushort port, Action<TurboListenOptions> configure);
    void Listen(string url);
    void Listen(string url, Action<TurboListenOptions> configure);
    void ListenLocalhost(ushort port);
    void ListenLocalhost(ushort port, Action<TurboListenOptions> configure);
    void ListenAnyIP(ushort port);
    void ListenAnyIP(ushort port, Action<TurboListenOptions> configure);
    void BindTcp(string host, ushort port);
    void Bind(TcpListenerOptions options);
    void Bind(QuicListenerOptions options);
    void Bind(ListenerOptions options, IListenerFactory factory);
    void ConfigureHttpsDefaults(Action<GaudiHttpsOptions> configure);
    void ConfigureEndpointDefaults(Action<TurboListenOptions> configure);

    IList<ListenerBinding> Endpoints { get; }  // read-only, populated by Bind() overloads only
    IList<string> Urls { get; }                // mutable list — add URL strings manually or via hosting configuration; resolved to bindings at startup
}
```

---

## Server Limits

```csharp
public sealed class GaudiServerLimits
{
    int MaxConcurrentConnections { get; set; }              // default: 0 (unlimited)
    long MaxRequestBodySize { get; set; }                   // default: 30,000,000 (~28.6 MiB, matching Kestrel)
    int MaxRequestHeaderCount { get; set; }                 // default: 100
    int MaxRequestHeadersTotalSize { get; set; }            // default: 32 * 1024
    long MaxResponseBufferSize { get; set; }                // default: 64 * 1024 (per-stream response write buffer)
    long? MaxRequestBufferSize { get; set; }                // default: 1 MiB (transport input buffer before backpressure; null = unlimited)
    int MaxResetStreamsPerWindow { get; set; }               // default: 200 (HTTP/2 Rapid Reset / CVE-2023-44487 mitigation; 0 = disabled)
    TimeSpan KeepAliveTimeout { get; set; }                 // default: 130s
    TimeSpan RequestHeadersTimeout { get; set; }            // default: 30s
    double MinRequestBodyDataRate { get; set; }             // default: 240
    TimeSpan MinRequestBodyDataRateGracePeriod { get; set; } // default: 5s
    double MinResponseDataRate { get; set; }                // default: 240
    TimeSpan MinResponseDataRateGracePeriod { get; set; }   // default: 5s
}
```

---

## Listen Options

```csharp
public sealed class TurboListenOptions(IPAddress address, ushort port)
{
    IPAddress Address { get; }
    ushort Port { get; }
    HttpProtocols Protocols { get; set; }  // default: Http1AndHttp2
    TransportBufferOptions? Transport { get; set; }  // default: null (protocol-optimized defaults)

    void UseHttps();
    void UseHttps(X509Certificate2 certificate);
    void UseHttps(string path, string? password = null);
    void UseHttps(Action<GaudiHttpsOptions> configure);
    void UseHttps(X509Certificate2 certificate, Action<GaudiHttpsOptions> configure);
    void UseHttps(string path, string? password, Action<GaudiHttpsOptions> configure);
    void UseConnectionLogging();
    void UseConnectionLogging(string loggerName);
}
```

---

## Transport Buffer Options

Controls backpressure thresholds on the read/write pipes between the OS socket and the HTTP pipeline. Applied per-connection for TCP and per-stream for QUIC. Set via `TurboListenOptions.Transport`. Every property is nullable — properties left at `null` fall back to the protocol-optimized default individually, so you only need to set the thresholds you want to change. A resume threshold above its pause threshold fails endpoint resolution with `InvalidOperationException`.

```csharp
public sealed class TransportBufferOptions
{
    long? InputPauseThreshold { get; set; }    // bytes buffered on the read pipe before the OS socket is paused
    long? InputResumeThreshold { get; set; }   // buffered byte count at which reading resumes (must be <= pause threshold)
    long? OutputPauseThreshold { get; set; }   // bytes buffered on the write pipe before the HTTP pipeline is paused
    long? OutputResumeThreshold { get; set; }  // must be <= OutputPauseThreshold
    int? MinimumSegmentSize { get; set; }      // minimum pipe buffer segment size
    int? ReceiveBufferHint { get; set; }       // size hint for PipeWriter.GetMemory on the receive path
}
```

Protocol-specific defaults applied for `null` properties (and when `Transport` itself is `null`):

| Property | TCP (one pipe per connection) | QUIC (one pipe per stream) |
|----------|------------------------------|----------------------------|
| `InputPauseThreshold` | 1 MiB | 64 KiB |
| `InputResumeThreshold` | 512 KiB | 32 KiB |
| `OutputPauseThreshold` | 64 KiB | 64 KiB |
| `OutputResumeThreshold` | 32 KiB | 32 KiB |
| `MinimumSegmentSize` | 16 KiB | 4 KiB |

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

---

## HTTPS Options

```csharp
public sealed class GaudiHttpsOptions
{
    X509Certificate2? ServerCertificate { get; set; }
    string? CertificatePath { get; set; }
    string? CertificatePassword { get; set; }
    SslProtocols EnabledSslProtocols { get; set; }                          // default: None (OS default)
    RemoteCertificateValidationCallback? ClientCertificateValidationCallback { get; set; }
    TimeSpan HandshakeTimeout { get; set; }                                 // default: 10s
    ClientCertificateMode ClientCertificateMode { get; set; }               // default: NoCertificate
    Func<string?, X509Certificate2?>? ServerCertificateSelector { get; set; }
}
```

---

## HTTP Protocols

```csharp
[Flags]
public enum HttpProtocols
{
    None = 0,
    Http1 = 1,
    Http2 = 2,
    Http1AndHttp2 = Http1 | Http2,
    Http3 = 4
}
```

---

## HTTP/1.x Options

```csharp
public sealed class Http1ServerOptions
{
    int MaxRequestLineLength { get; set; }    // default: 8 * 1024
    int MaxRequestTargetLength { get; set; }  // default: 8 * 1024
    int MaxPipelinedRequests { get; set; }    // default: 16
    int MaxChunkExtensionLength { get; set; } // default: 4 * 1024
    int MaxBufferedRequestBodySize { get; set; } // default: 64 * 1024 (bodies up to this size buffered in memory, larger streamed)
    TimeSpan BodyReadTimeout { get; set; }    // default: 30s
    int? MaxHeaderListSize { get; set; }                      // default: null (uses Limits.MaxRequestHeadersTotalSize)
    long? MaxRequestBodySize { get; set; }                    // default: null (uses Limits)
    TimeSpan? KeepAliveTimeout { get; set; }                  // default: null (uses Limits)
    TimeSpan? RequestHeadersTimeout { get; set; }             // default: null (uses Limits)
    double? MinRequestBodyDataRate { get; set; }              // default: null (uses Limits)
    TimeSpan? MinRequestBodyDataRateGracePeriod { get; set; } // default: null (uses Limits)
    double? MinResponseDataRate { get; set; }                 // default: null (uses Limits)
    TimeSpan? MinResponseDataRateGracePeriod { get; set; }    // default: null (uses Limits)
}
```

---

## HTTP/2 Options

```csharp
public sealed class Http2ServerOptions
{
    int MaxConcurrentStreams { get; set; }            // default: 100
    int InitialConnectionWindowSize { get; set; }    // default: 1 * 1024 * 1024
    int InitialStreamWindowSize { get; set; }        // default: 768 * 1024
    int MaxStreamWindowSize { get; set; }            // default: 8 * 1024 * 1024 (adaptive scaling upper bound)
    double WindowScaleThresholdMultiplier { get; set; } // default: 1.0
    bool EnableAdaptiveWindowScaling { get; set; }   // default: true (BDP-based receive-window growth)
    int MaxFrameSize { get; set; }                   // default: 16 * 1024
    int HeaderTableSize { get; set; }                // default: 4 * 1024
    int? MaxHeaderListSize { get; set; }             // default: null (uses Limits.MaxRequestHeadersTotalSize)
    long? MaxResponseBufferSize { get; set; }        // default: null (uses Limits.MaxResponseBufferSize)
    TimeSpan KeepAlivePingDelay { get; set; }        // default: infinite (server-initiated keep-alive PINGs disabled)
    TimeSpan KeepAlivePingTimeout { get; set; }      // default: 20s (max wait for PING ACK before closing)
    long? MaxRequestBodySize { get; set; }                    // default: null (uses Limits)
    TimeSpan? KeepAliveTimeout { get; set; }                  // default: null (uses Limits)
    TimeSpan? RequestHeadersTimeout { get; set; }             // default: null (uses Limits)
    double? MinRequestBodyDataRate { get; set; }              // default: null (uses Limits)
    TimeSpan? MinRequestBodyDataRateGracePeriod { get; set; } // default: null (uses Limits)
    double? MinResponseDataRate { get; set; }                 // default: null (uses Limits)
    TimeSpan? MinResponseDataRateGracePeriod { get; set; }    // default: null (uses Limits)
}
```

---

## HTTP/3 Options

```csharp
public sealed class Http3ServerOptions
{
    int MaxConcurrentStreams { get; set; }    // default: 100
    int? MaxHeaderListSize { get; set; }      // default: null (uses Limits.MaxRequestHeadersTotalSize)
    int QpackMaxTableCapacity { get; set; }   // default: 0
    int QpackBlockedStreams { get; set; }     // default: 100
    long? MaxResponseBufferSize { get; set; } // default: null (uses Limits.MaxResponseBufferSize)
    long? MaxRequestBodySize { get; set; }                    // default: null (uses Limits)
    TimeSpan? KeepAliveTimeout { get; set; }                  // default: null (uses Limits)
    TimeSpan? RequestHeadersTimeout { get; set; }             // default: null (uses Limits)
    double? MinRequestBodyDataRate { get; set; }              // default: null (uses Limits)
    TimeSpan? MinRequestBodyDataRateGracePeriod { get; set; } // default: null (uses Limits)
    double? MinResponseDataRate { get; set; }                 // default: null (uses Limits)
    TimeSpan? MinResponseDataRateGracePeriod { get; set; }    // default: null (uses Limits)
}
```
