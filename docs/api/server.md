# Server API

TurboHTTP Server is an `IServer` implementation for ASP.NET Core built on Akka.Streams. It replaces Kestrel as the transport layer.

## Registration

```csharp
public static class TurboServerWebHostBuilderExtensions
{
    IHostBuilder UseTurboHttp(
        this IHostBuilder builder,
        Action<TurboServerOptions>? configure = null);
}
```

Register the server during application setup:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseTurboHttp(options =>
{
    options.ListenLocalhost(5100);
});

var app = builder.Build();
app.MapGet("/", () => "Hello from TurboHTTP!");
await app.RunAsync();
```

---

## TurboServer

```csharp
public sealed class TurboServer : IServer
{
    IFeatureCollection Features { get; }

    Task StartAsync<TContext>(
        IHttpApplication<TContext> application,
        CancellationToken cancellationToken) where TContext : notnull;

    Task StopAsync(CancellationToken cancellationToken);
}
```

`TurboServer` implements `IServer` from `Microsoft.AspNetCore.Hosting.Server`. It creates or reuses an `ActorSystem`, materializes the Akka.Streams pipeline, and spawns the actor hierarchy for connection management.

---

## Server Options

```csharp
public sealed class TurboServerOptions
{
    TurboServerLimits Limits { get; }

    TimeSpan GracefulShutdownTimeout { get; set; }  // default: 30s
    TimeSpan HandlerTimeout { get; set; }            // default: 30s
    TimeSpan HandlerGracePeriod { get; set; }        // default: 5s

    int RequestBodyBufferThreshold { get; set; }     // default: 64 * 1024
    TimeSpan BodyConsumptionTimeout { get; set; }    // default: 30s
    int ResponseBodyChunkSize { get; set; }          // default: 16 * 1024

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
    void ConfigureHttpsDefaults(Action<TurboHttpsOptions> configure);
    void ConfigureEndpointDefaults(Action<TurboListenOptions> configure);

    IList<ListenerBinding> Endpoints { get; }  // read-only, populated by Bind() overloads only
    IList<string> Urls { get; }                // read-only, resolved to bindings at startup (add strings manually or via hosting configuration)
}
```

---

## Server Limits

```csharp
public sealed class TurboServerLimits
{
    int MaxConcurrentConnections { get; set; }              // default: 0 (unlimited)
    int MaxConcurrentRequests { get; set; }                 // default: 0 (unlimited)
    int MinRequestGuarantee { get; set; }                   // default: 10
    long MaxRequestBodySize { get; set; }                   // default: 30 * 1024 * 1024
    int MaxRequestHeaderCount { get; set; }                 // default: 100
    int MaxRequestHeadersTotalSize { get; set; }            // default: 32 * 1024
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

    void UseHttps();
    void UseHttps(X509Certificate2 certificate);
    void UseHttps(string path, string? password = null);
    void UseHttps(Action<TurboHttpsOptions> configure);
    void UseHttps(X509Certificate2 certificate, Action<TurboHttpsOptions> configure);
    void UseHttps(string path, string? password, Action<TurboHttpsOptions> configure);
    void UseConnectionLogging();
    void UseConnectionLogging(string loggerName);
}
```

---

## HTTPS Options

```csharp
public sealed class TurboHttpsOptions
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
    int MaxFrameSize { get; set; }                   // default: 16 * 1024
    int HeaderTableSize { get; set; }                // default: 4 * 1024
    int? MaxHeaderListSize { get; set; }             // default: null (uses Limits.MaxRequestHeadersTotalSize)
    long MaxResponseBufferSize { get; set; }         // default: 64 * 1024
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
    long? MaxRequestBodySize { get; set; }                    // default: null (uses Limits)
    TimeSpan? KeepAliveTimeout { get; set; }                  // default: null (uses Limits)
    TimeSpan? RequestHeadersTimeout { get; set; }             // default: null (uses Limits)
    double? MinRequestBodyDataRate { get; set; }              // default: null (uses Limits)
    TimeSpan? MinRequestBodyDataRateGracePeriod { get; set; } // default: null (uses Limits)
    double? MinResponseDataRate { get; set; }                 // default: null (uses Limits)
    TimeSpan? MinResponseDataRateGracePeriod { get; set; }    // default: null (uses Limits)
}
```
