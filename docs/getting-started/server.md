# Server Quick Start

Build a working GaudiHTTP server in under 5 minutes. GaudiHTTP replaces Kestrel as a drop-in `IServer` implementation — everything above the transport layer is standard ASP.NET Core.

## 1. Create a Project

```bash
dotnet new web -n MyGaudiApp
cd MyGaudiApp
dotnet add package GaudiHTTP
```

## 2. Configure the Server

In `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseGaudiHttp(options =>
{
    options.ListenLocalhost(5100);
});

var app = builder.Build();

app.MapGet("/health", () => new { status = "healthy" });

app.MapGet("/", () => "Hello from GaudiHTTP!");

await app.RunAsync();
```

`UseGaudiHttp()` registers `GaudiServer` as the `IServer` implementation. After that, you use standard ASP.NET Core — `app.MapGet`, `app.UseRouting`, middleware, controllers, minimal APIs — everything works as you'd expect.

## 3. Test It

```bash
dotnet run

# In another terminal:
curl http://localhost:5100/health
# {"status":"healthy"}

curl http://localhost:5100/
# Hello from GaudiHTTP!
```

## 4. Add HTTPS

```csharp
using GaudiHTTP.Server;

builder.Host.UseGaudiHttp(options =>
{
    options.ListenLocalhost(5100);
    options.ListenLocalhost(5101, listen =>
    {
        listen.UseHttps();
        listen.Protocols = HttpProtocols.Http1AndHttp2;
    });
});
```

## What's Different from Kestrel?

GaudiHTTP is a transport-level replacement — it handles TCP/QUIC connections, protocol negotiation, and HTTP wire format. Your ASP.NET Core code stays the same.

| | Kestrel | GaudiHTTP |
|---|---------|-----------|
| Transport | Sockets (Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets) | SocketPipeConnection (TCP and QUIC) |
| Connection model | Thread pool | Actor per connection |
| Protocols | HTTP/1.1, HTTP/2, HTTP/3 | HTTP/1.0, HTTP/1.1, HTTP/2, HTTP/3 |
| Backpressure | Pipe-based | Akka.Streams reactive streams |
| Shutdown | IHostApplicationLifetime | Akka Coordinated Shutdown |

## Next Steps

- [Installation & Setup](/server/installation) — endpoints, HTTPS, certificates
- [Configuration](/server/configuration) — all server options
- [Using with ASP.NET Core](/server/aspnet-core) — middleware, routing, DI guidance
- [Hosting & Lifecycle](/server/hosting) — actor hierarchy, graceful shutdown
