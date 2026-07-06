using System.Net;
using System.Net.Quic;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GaudiHTTP.IntegrationTests.Client.Shared;

internal sealed class KestrelTestBackend : ITestBackend
{
    private WebApplication? _app;

    public int HttpPort { get; private set; }
    public int HttpsPort { get; private set; }
    public int QuicPort { get; private set; }
    public bool IsQuicAvailable { get; private set; }
    public bool IsHttp10TlsSupported => true;
    public bool HasCustomEndpoints => true;

    public async Task StartAsync()
    {
        var cert = LoadCertificate();
        var quicSupported = QuicListener.IsSupported;

        // The reserve→listen gap is a race, and lingering TIME_WAIT pairs from a previous
        // test-host process can still fail Kestrel's explicit bind: retry with FRESH ports
        // instead of failing the whole suite (an AddressInUse here wipes out every test).
        for (var attempt = 0; ; attempt++)
        {
            // Listen on BOTH loopback families for each port: TLS clients resolve "localhost",
            // try ::1 first, and Windows silently drops SYNs to non-listening ::1 ports (~2s
            // penalty per connect, >12s under load) instead of refusing them. IPv4-only
            // listeners therefore cause sporadic test timeouts. Ports are pre-allocated
            // because Kestrel cannot share a dynamic port across two Listen calls.
            var httpPort = ReserveLoopbackPort();
            var httpsPort = ReserveLoopbackPort();

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();

            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
                {
                    kestrel.Listen(address, httpPort, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http1;
                    });

                    kestrel.Listen(address, httpsPort, listenOptions =>
                    {
                        listenOptions.Protocols = quicSupported
                            ? HttpProtocols.Http1AndHttp2AndHttp3
                            : HttpProtocols.Http1AndHttp2;
                        listenOptions.UseHttps(cert);
                    });
                }
            });

            var app = builder.Build();
            HttpbinEndpoints.Map(app);

            try
            {
                await app.StartAsync();
                _app = app;
                break;
            }
            catch (IOException ex) when (attempt < 4)
            {
                await Console.Error.WriteLineAsync(
                    $"[KestrelTestBackend] bind failed on ports {httpPort}/{httpsPort} (attempt {attempt + 1}): {ex.Message} — retrying with fresh ports");
                await app.DisposeAsync();
            }
        }

        ResolvePortsFromServer(_app);

        if (quicSupported && HttpsPort > 0)
        {
            QuicPort = HttpsPort;
            IsQuicAvailable = await ProbeQuicAsync(QuicPort);
            if (!IsQuicAvailable)
            {
                QuicPort = 0;
            }
        }

        await Console.Error.WriteLineAsync(
            $"[KestrelTestBackend] HTTP={HttpPort} HTTPS={HttpsPort} QUIC={QuicPort} (available={IsQuicAvailable})");
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static int ReserveLoopbackPort()
    {
        // Do NOT bind port 0 to pick a port: Windows' ephemeral allocator hands back the most
        // recently FREED port, so consecutive test-host runs converge on the same port whose
        // TIME_WAIT connection pairs from the previous run then fail Kestrel's explicit bind
        // for minutes (observed as full-suite AddressInUse wipeouts). Pick a random port below
        // the dynamic client range (49152+) instead, so client-connection churn never collides,
        // and verify it explicitly binds on BOTH loopback families — the same bind Kestrel does.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var port = Random.Shared.Next(20000, 45000);

            try
            {
                using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                probe.Bind(new IPEndPoint(IPAddress.Loopback, port));
                using var probe6 = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
                probe6.Bind(new IPEndPoint(IPAddress.IPv6Loopback, port));
                return port;
            }
            catch (SocketException)
            {
                // port taken (or excluded range) on either family — try another
            }
        }

        throw new InvalidOperationException("Could not reserve a loopback port free on both address families.");
    }

    private void ResolvePortsFromServer(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addressFeature = server.Features.Get<IServerAddressesFeature>();
        if (addressFeature is null)
        {
            return;
        }

        foreach (var address in addressFeature.Addresses)
        {
            var uri = new Uri(address);

            if (uri.Scheme == "http" && HttpPort == 0)
            {
                HttpPort = uri.Port;
            }
            else if (uri.Scheme == "https" && HttpsPort == 0)
            {
                HttpsPort = uri.Port;
            }
        }
    }

    private static async Task<bool> ProbeQuicAsync(int port)
    {
        try
        {
            using var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            using var client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version30;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var response = await client.GetAsync($"https://127.0.0.1:{port}/get", cts.Token);
            await Console.Error.WriteLineAsync(
                $"[KestrelTestBackend] QUIC probe: status={response.StatusCode} version={response.Version}");
            return response.IsSuccessStatusCode && response.Version == HttpVersion.Version30;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[KestrelTestBackend] QUIC probe failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException is not null)
            {
                await Console.Error.WriteLineAsync(
                    $"[KestrelTestBackend]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
            return false;
        }
    }


    private static X509Certificate2 LoadCertificate()
    {
        var devCert = FindDevCert();
        if (devCert is not null)
        {
            return devCert;
        }

        CertificateManager.EnsureCertificatesExist();
        var certPath = Path.Combine(CertificateManager.SslDir, "cert.pem");
        var keyPath = Path.Combine(CertificateManager.SslDir, "key.pem");
        var pem = X509Certificate2.CreateFromPemFile(certPath, keyPath);
        return X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pfx), null);
    }

    private static X509Certificate2? FindDevCert()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var certs = store.Certificates
            .Find(X509FindType.FindByExtension, "1.3.6.1.4.1.311.84.1.1", validOnly: false);
        return certs.Count > 0 ? certs[0] : null;
    }
}
