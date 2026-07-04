using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using GaudiHTTP.Server;

namespace GaudiHTTP.CpuProbeServer;

/// <summary>
/// Thin wrapper around an in-proc GaudiServer instance + two HttpClient instances
/// (one for HTTP/1.1, one for cleartext HTTP/2) used to drive the CpuProbeServer harness.
/// The load client is HttpClient (not GaudiHttp) so the measurement isolates GaudiServer.
/// </summary>
internal sealed class ServerProbeFixture : IAsyncDisposable
{
    // 1 KiB deterministic response body — same pattern as benchmark payloads.
    private static readonly byte[] ProbeResponse = GenerateBody(1 * 1024);

    private readonly WebApplication _app;
    private readonly HttpClient _h11Client;
    private readonly HttpClient _h2Client;
    private readonly Uri _h11BaseUri;
    private readonly Uri _h2BaseUri;

    private ServerProbeFixture(
        WebApplication app,
        Uri h11BaseUri,
        Uri h2BaseUri,
        HttpClient h11Client,
        HttpClient h2Client)
    {
        _app = app;
        _h11BaseUri = h11BaseUri;
        _h2BaseUri = h2BaseUri;
        _h11Client = h11Client;
        _h2Client = h2Client;
    }

    /// <summary>
    /// Starts an in-proc GaudiServer with HTTP/1.1 and cleartext HTTP/2 listeners,
    /// builds one HttpClient per protocol, and returns the ready-to-hammer fixture.
    /// </summary>
    public static async Task<ServerProbeFixture> StartAsync()
    {
        // Required for h2c (cleartext HTTP/2) with SocketsHttpHandler.
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        // Raise ThreadPool minimums so GaudiServer and SocketsHttpHandler
        // have enough threads under high-concurrency probing (matching benchmark setup).
        ThreadPool.GetMinThreads(out var workers, out var io);
        ThreadPool.SetMinThreads(Math.Max(workers, 1024), Math.Max(io, 1024));

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        builder.Host.UseGaudiHttp(options =>
        {
            // HTTP/1.1-only listener on ephemeral port
            options.Listen(IPAddress.Loopback, 0, lo =>
                lo.Protocols = HttpProtocols.Http1);

            // HTTP/2 cleartext (h2c) prior-knowledge listener on ephemeral port
            options.Listen(IPAddress.Loopback, 0, lo =>
                lo.Protocols = HttpProtocols.Http2);

            // Raise H/2 limits to support CL=512 in the probe
            options.Http2.MaxConcurrentStreams = 512;
            options.Http2.InitialConnectionWindowSize = 4 * 1024 * 1024;
            options.Http2.InitialStreamWindowSize = 1 * 1024 * 1024;
        });

        var app = builder.Build();

        // /probe returns a fixed 1 KiB body for every GET
        app.MapGet("/probe", () => Results.Bytes(ProbeResponse, "application/octet-stream"));

        await app.StartAsync();

        // GaudiServer populates IServerAddressesFeature in listener-registration order:
        // index 0 = HTTP/1.1 endpoint, index 1 = HTTP/2 endpoint
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .Select(a => new Uri(a))
            .ToArray();

        var h11BaseUri = new Uri(string.Concat("http://127.0.0.1:", addresses[0].Port.ToString()));
        var h2BaseUri = new Uri(string.Concat("http://127.0.0.1:", addresses[1].Port.ToString()));

        var h11Client = BuildHttpClient(
            version: HttpVersion.Version11,
            versionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            maxConnectionsPerServer: 512,
            enableMultipleH2Connections: false);
        h11Client.BaseAddress = h11BaseUri;

        var h2Client = BuildHttpClient(
            version: HttpVersion.Version20,
            versionPolicy: HttpVersionPolicy.RequestVersionExact,
            maxConnectionsPerServer: 16,
            enableMultipleH2Connections: true);
        h2Client.BaseAddress = h2BaseUri;

        return new ServerProbeFixture(app, h11BaseUri, h2BaseUri, h11Client, h2Client);
    }

    /// <summary>
    /// Issues <paramref name="count"/> sequential GET /probe requests over the chosen protocol.
    /// The response body is consumed to completion on each request.
    /// </summary>
    public async Task HammerAsync(string protocol, int count, CancellationToken ct)
    {
        var client = protocol == "h2" ? _h2Client : _h11Client;
        var baseUri = protocol == "h2" ? _h2BaseUri : _h11BaseUri;
        var probeUri = new Uri(baseUri, "/probe");

        for (var i = 0; i < count; i++)
        {
            using var response = await client.GetAsync(probeUri, ct);
            response.EnsureSuccessStatusCode();
            // Consume the body so the connection is released back to the pool
            await response.Content.ReadAsByteArrayAsync(ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _h11Client.Dispose();
        _h2Client.Dispose();

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static HttpClient BuildHttpClient(
        Version version,
        HttpVersionPolicy versionPolicy,
        int maxConnectionsPerServer,
        bool enableMultipleH2Connections)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            MaxConnectionsPerServer = maxConnectionsPerServer,
            EnableMultipleHttp2Connections = enableMultipleH2Connections,
        };

        return new HttpClient(handler)
        {
            DefaultRequestVersion = version,
            DefaultVersionPolicy = versionPolicy,
            Timeout = TimeSpan.FromMinutes(5),
        };
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
}
