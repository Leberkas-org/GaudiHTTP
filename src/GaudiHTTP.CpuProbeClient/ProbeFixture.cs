using System.Net;
using Akka.Actor;
using GaudiHTTP.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GaudiHTTP.CpuProbeClient;

/// <summary>
/// Thin wrapper around an in-proc Kestrel server + two GaudiHttpClient instances
/// (one for HTTP/1.1, one for cleartext HTTP/2) used to drive the CpuProbe harness.
/// </summary>
internal sealed class ProbeFixture : IAsyncDisposable
{
    // 1 KiB deterministic response body — same pattern as benchmark payloads.
    private static readonly byte[] ProbeResponse = GenerateBody(1 * 1024);

    private readonly WebApplication _app;
    private readonly IGaudiHttpClient _h11Client;
    private readonly IGaudiHttpClient _h2Client;
    private readonly ServiceProvider _h11Provider;
    private readonly ServiceProvider _h2Provider;
    private readonly ActorSystem _h11System;
    private readonly ActorSystem _h2System;
    private readonly Uri _h11BaseUri;
    private readonly Uri _h2BaseUri;

    private ProbeFixture(
        WebApplication app,
        Uri h11BaseUri,
        Uri h2BaseUri,
        IGaudiHttpClient h11Client,
        ServiceProvider h11Provider,
        ActorSystem h11System,
        IGaudiHttpClient h2Client,
        ServiceProvider h2Provider,
        ActorSystem h2System)
    {
        _app = app;
        _h11BaseUri = h11BaseUri;
        _h2BaseUri = h2BaseUri;
        _h11Client = h11Client;
        _h11Provider = h11Provider;
        _h11System = h11System;
        _h2Client = h2Client;
        _h2Provider = h2Provider;
        _h2System = h2System;
    }

    /// <summary>
    /// Starts the in-proc Kestrel server with HTTP/1.1 and h2c listeners, builds
    /// one GaudiHttpClient per protocol, and returns the ready-to-hammer fixture.
    /// </summary>
    public static async Task<ProbeFixture> StartAsync()
    {
        // Raise ThreadPool minimums so Akka dispatchers and SocketsHttpHandler
        // have enough threads under high-concurrency probing (matching benchmark setup).
        ThreadPool.GetMinThreads(out var workers, out var io);
        ThreadPool.SetMinThreads(Math.Max(workers, 1024), Math.Max(io, 1024));

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        builder.Services.Configure<KestrelServerOptions>(options =>
        {
            // HTTP/1.1-only listener on ephemeral port
            options.Listen(IPAddress.Loopback, 0, lo =>
                lo.Protocols = HttpProtocols.Http1);

            // HTTP/2 cleartext (h2c) prior-knowledge listener on ephemeral port
            options.Listen(IPAddress.Loopback, 0, lo =>
                lo.Protocols = HttpProtocols.Http2);

            // Raise H/2 limits to support CL=512 in the probe
            options.Limits.Http2.MaxStreamsPerConnection = 512;
            options.Limits.Http2.InitialConnectionWindowSize = 4 * 1024 * 1024;
            options.Limits.Http2.InitialStreamWindowSize = 1 * 1024 * 1024;
            options.Limits.Http2.MaxFrameSize = 256 * 1024;

            options.Limits.MaxConcurrentConnections = null;
            options.Limits.MaxConcurrentUpgradedConnections = null;
        });

        var app = builder.Build();

        // /probe returns a fixed 1 KiB body for every GET
        app.MapGet("/probe", () => Results.Bytes(ProbeResponse, "application/octet-stream"));

        await app.StartAsync();

        // Kestrel returns addresses in listener-registration order:
        // index 0 = HTTP/1.1, index 1 = HTTP/2
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .Select(a => new Uri(a))
            .ToArray();

        var h11Port = addresses[0].Port;
        var h2Port = addresses[1].Port;

        var h11BaseUri = new Uri($"http://127.0.0.1:{h11Port}");
        var h2BaseUri = new Uri($"http://127.0.0.1:{h2Port}");

        var (h11Client, h11Provider, h11System) = BuildClient(h11BaseUri, HttpVersion.Version11);
        var (h2Client, h2Provider, h2System) = BuildClient(h2BaseUri, HttpVersion.Version20);

        return new ProbeFixture(
            app,
            h11BaseUri, h2BaseUri,
            h11Client, h11Provider, h11System,
            h2Client, h2Provider, h2System);
    }

    /// <summary>
    /// Issues <paramref name="count"/> sequential GET /probe requests over the chosen protocol.
    /// </summary>
    public async Task HammerAsync(string protocol, int count, CancellationToken ct)
    {
        var client = protocol == "h2" ? _h2Client : _h11Client;
        var baseUri = protocol == "h2" ? _h2BaseUri : _h11BaseUri;
        var probeUri = new Uri(baseUri, "/probe");

        for (var i = 0; i < count; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, probeUri);
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            // Consume the body so the connection is released back to the pool
            await response.Content.ReadAsByteArrayAsync(ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeClientAsync(_h11Client, _h11System, _h11Provider);
        await DisposeClientAsync(_h2Client, _h2System, _h2Provider);

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static (IGaudiHttpClient client, ServiceProvider provider, ActorSystem system)
        BuildClient(Uri baseAddress, Version version)
    {
        // Prevent the ActorSystem from calling Environment.Exit or hooking AppDomain.ProcessExit
        // — the probe process must outlive all measurement cells.
        var hocon = Akka.Configuration.ConfigurationFactory.ParseString("""
            akka.coordinated-shutdown.exit-clr = off
            akka.coordinated-shutdown.run-by-clr-shutdown = off
            """);
        var system = ActorSystem.Create($"GaudiProbe-{Guid.NewGuid():N}", hocon);

        var options = new GaudiClientOptions
        {
            BaseAddress = baseAddress,
            RequestBodyChunkSize = 64 * 1024,
            // H1.x: 512 connections × 64 pipeline depth to handle CL=512
            Http1 = new Http1ClientOptions
            {
                MaxConnectionsPerServer = 512,
                MaxPipelineDepth = 64
            },
            // H2: 16 connections × 512 streams = 8192 in-flight capacity
            Http2 = new Http2ClientOptions
            {
                MaxConnectionsPerServer = 16,
                MaxConcurrentStreams = 512
            },
        };

        var services = new ServiceCollection();
        services.AddSingleton(system);
        services.AddGaudiHttpClient();
        services.Replace(ServiceDescriptor.Singleton<IOptionsFactory<GaudiClientOptions>>(
            new FixedOptionsFactory(options)));

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IGaudiHttpClientFactory>();
        var client = factory.CreateClient(string.Empty);
        client.BaseAddress = baseAddress;
        client.DefaultRequestVersion = version;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        client.Timeout = TimeSpan.FromMinutes(5);

        return (client, provider, system);
    }

    private static async Task DisposeClientAsync(
        IGaudiHttpClient client,
        ActorSystem system,
        ServiceProvider provider)
    {
        client.Requests.TryComplete();

        try
        {
            await client.Responses.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Pipeline may complete with an error during shutdown — that is fine.
        }

        client.Dispose();

        try
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // Termination may time out under high load — continue so ServiceProvider is disposed.
        }

        try
        {
            await system.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Same rationale.
        }

        await Task.Delay(TimeSpan.FromMilliseconds(250));

        await provider.DisposeAsync();
    }

    private static byte[] GenerateBody(int sizeBytes)
    {
        var body = new byte[sizeBytes];
        for (var i = 0; i < sizeBytes; i++)
        {
            body[i] = (byte)(i % 256);
        }

        return body;
    }

    private sealed class FixedOptionsFactory(GaudiClientOptions options) : IOptionsFactory<GaudiClientOptions>
    {
        public GaudiClientOptions Create(string name) => options;
    }
}
