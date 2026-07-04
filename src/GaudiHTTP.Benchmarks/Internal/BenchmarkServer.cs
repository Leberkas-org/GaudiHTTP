using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Quic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GaudiHTTP.Benchmarks.Internal;

public sealed class BenchmarkServer : IAsyncDisposable
{
    private WebApplication? _app;
    private X509Certificate2? _cert;

    public int Http11Port { get; private set; }

    public int Http20Port { get; private set; }

    public int Http30Port { get; private set; }

    public bool IsQuicAvailable { get; private set; }

    public async ValueTask InitializeAsync(IAllocationProfiler? profiler = null)
    {
        _cert = GenerateSelfSignedCert();

        var quicAvailable = QuicListenerIsSupported();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        var cert = _cert;
        builder.Services.Configure<KestrelServerOptions>(options =>
        {
            // HTTP/1.1-only listener
            options.Listen(IPAddress.Loopback, 0, lo =>
                lo.Protocols = HttpProtocols.Http1);

            // HTTP/2 cleartext (h2c) prior-knowledge listener
            options.Listen(IPAddress.Loopback, 0, lo =>
                lo.Protocols = HttpProtocols.Http2);

            if (quicAvailable)
            {
                // HTTP/3 (QUIC+TLS) listener
                options.Listen(IPAddress.Loopback, 0, lo =>
                {
                    lo.Protocols = HttpProtocols.Http3;
                    lo.UseHttps(cert);
                });
            }

            // Raise HTTP/2 limits to support high-concurrency benchmarks (CL=256+).
            options.Limits.Http2.MaxStreamsPerConnection = 512;
            options.Limits.Http2.InitialConnectionWindowSize = 4 * 1024 * 1024;
            options.Limits.Http2.InitialStreamWindowSize = 1024 * 1024;
            options.Limits.Http2.MaxFrameSize = 256 * 1024;

            // Raise general limits for HTTP/3 high-concurrency benchmarks.
            options.Limits.MaxConcurrentConnections = null;
            options.Limits.MaxConcurrentUpgradedConnections = null;
        });

        if (quicAvailable)
        {
            builder.WebHost.UseQuic(quic =>
            {
                quic.MaxBidirectionalStreamCount = 512;
                quic.MaxUnidirectionalStreamCount = 32;
            });
        }

        var app = builder.Build();

        RegisterRoutes(app, profiler);

        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .ToArray();

        Http11Port = new Uri(addresses[0]).Port;
        Http20Port = new Uri(addresses[1]).Port;
        Http30Port = quicAvailable ? new Uri(addresses[2]).Port : 0;
        IsQuicAvailable = quicAvailable;

        _app = app;
    }

    private static bool QuicListenerIsSupported()
    {
        try
        {
            var type = Type.GetType("System.Net.Quic.QuicListener, System.Net.Quic");
            if (type is null)
            {
                return false;
            }

            var prop = type.GetProperty("IsSupported", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            return prop?.GetValue(null) is true;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        _cert?.Dispose();
    }

    private static X509Certificate2 GenerateSelfSignedCert()
    {
        using var key = RSA.Create(2048);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        var request = new CertificateRequest(
            "CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(san.Build());
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
    }

    private static void RegisterRoutes(WebApplication app, IAllocationProfiler? profiler)
    {
        BenchmarkRoutes.Register(app, profiler);
    }
}
