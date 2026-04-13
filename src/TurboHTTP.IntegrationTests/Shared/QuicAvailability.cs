using System.Net;
using System.Net.Quic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1416

namespace TurboHTTP.IntegrationTests.Shared;

/// <summary>
/// One-time runtime probe that checks whether QUIC/HTTP3 actually works on this machine.
/// <see cref="QuicConnection.IsSupported"/> can return <c>true</c> even when UDP loopback
/// is blocked by group policy or firewall (common on Windows Enterprise). This helper
/// starts an ephemeral Kestrel server with HTTP/3 and attempts a real connection.
/// </summary>
internal static class QuicAvailability
{
    private static readonly Lazy<bool> IsAvailableLazy = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Returns true if HTTP/3 over QUIC actually works on this machine.</summary>
    private static bool IsAvailable => IsAvailableLazy.Value;

    /// <summary>Skips the calling test if QUIC is not functionally available.</summary>
    public static void SkipIfUnavailable()
    {
        if (!IsAvailable)
        {
            Assert.Skip("QUIC/HTTP3 is not functionally available on this platform " +
                        "(UDP loopback may be blocked by firewall or group policy).");
        }
    }

    private static bool Probe()
    {
        if (!QuicConnection.IsSupported)
        {
            return false;
        }

        try
        {
            return ProbeAsync().GetAwaiter().GetResult();
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ProbeAsync()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=quic-probe", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        req.CertificateExtensions.Add(san.Build());
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddHours(1));
        cert = X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);

        // Kestrel does not support dynamic port binding (port 0) when multiple transports
        // are enabled. With Http1AndHttp2AndHttp3, port 0 silently disables HTTP/3.
        // Resolve a free port via a temporary TCP listener first.
        var port = GetFreePort();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port, lo =>
            {
                lo.UseHttps(cert);
                lo.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
            });
        });

        var app = builder.Build();
        app.MapGet("/quic-probe", () => "ok");
        await app.StartAsync();

        try
        {
            using var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            using var client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version30;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await client.GetAsync($"https://127.0.0.1:{port}/quic-probe", cts.Token);
            return response.IsSuccessStatusCode && response.Version == HttpVersion.Version30;
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}