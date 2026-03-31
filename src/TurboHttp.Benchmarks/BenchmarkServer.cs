using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TurboHttp.Benchmarks;

/// <summary>
/// Minimal Kestrel test server for benchmarking both HttpClient and TurboHttp.
/// Supports HTTP/1.1 and HTTP/2 on a dynamic port on 127.0.0.1.
/// Exposes two simple benchmark routes with keep-alive enabled.
/// </summary>
public sealed class BenchmarkServer : IAsyncDisposable
{
    private WebApplication? _app;

    /// <summary>Port on which the server is listening. Set after initialization.</summary>
    public int Port { get; private set; }

    /// <summary>
    /// Starts the Kestrel server on 127.0.0.1:0 (dynamic port).
    /// Call this once in GlobalSetup.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        // Configure Kestrel to listen on loopback with both HTTP/1.1 and HTTP/2
        builder.Services.Configure<KestrelServerOptions>(options =>
        {
            // HTTP/1.1 on dynamic port
            options.Listen(IPAddress.Loopback, 0, lo =>
                lo.Protocols = HttpProtocols.Http1);

            // HTTP/2 h2c (cleartext prior knowledge) on separate dynamic port
            // Note: Kestrel will assign different ports, or clients can upgrade via Alt-Svc
            options.Listen(IPAddress.Loopback, 0, lo =>
                lo.Protocols = HttpProtocols.Http2);
        });

        var app = builder.Build();

        RegisterRoutes(app);

        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.ToArray();

        // Extract port from first address (HTTP/1.1)
        Port = new Uri(addresses[0]).Port;

        _app = app;
    }

    /// <summary>
    /// Stops the server and cleans up resources.
    /// Call this in GlobalCleanup.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static void RegisterRoutes(WebApplication app)
    {
        // Simple benchmark endpoint: minimal response body, suitable for throughput testing
        app.MapGet("/benchmark/simple", () =>
            Results.Content("OK\n", "text/plain"));

        // Payload echo endpoint: accepts POST body and returns size received
        app.MapPost("/benchmark/payload", async ctx =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var received = ms.ToArray();
            var response = System.Text.Encoding.UTF8.GetBytes($"received:{received.Length}");
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength = response.Length;
            await ctx.Response.Body.WriteAsync(response);
        });
    }
}
