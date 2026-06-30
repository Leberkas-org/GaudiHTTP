using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using GaudiHTTP.Server;

namespace GaudiHTTP.Benchmarks.Internal;

public sealed class GaudiBenchmarkServer : IAsyncDisposable
{
    private WebApplication? _app;
    private X509Certificate2? _cert;

    public int Http11Port { get; private set; }
    public int Http20Port { get; private set; }
    public int Http30Port { get; private set; }

    public async ValueTask InitializeAsync(IAllocationProfiler? profiler = null)
    {
        _cert = GenerateSelfSignedCert();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        var cert = _cert;
        builder.Host.UseGaudiHttp(options =>
        {
            options.Listen(IPAddress.Loopback, 0, lo =>
                lo.Protocols = HttpProtocols.Http1);

            options.Listen(IPAddress.Loopback, 0, lo =>
                lo.Protocols = HttpProtocols.Http2);

            options.Listen(IPAddress.Loopback, 0, lo =>
            {
                lo.Protocols = HttpProtocols.Http3;
                lo.UseHttps(cert);
            });

            options.Http2.MaxConcurrentStreams = 512;
            options.Http2.InitialConnectionWindowSize = 4 * 1024 * 1024;
            options.Http2.InitialStreamWindowSize = 1 * 1024 * 1024;

            // Body handling matched to the client so request/response buffering and chunking
            // do not diverge between client and server benchmarks.
            options.ResponseBodyChunkSize = 64 * 1024;
            options.Http1.MaxBufferedRequestBodySize = 2 * 1024 * 1024;
        });

        var app = builder.Build();

        BenchmarkRoutes.Register(app, profiler);

        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .ToArray();

        Http11Port = new Uri(addresses[0]).Port;
        Http20Port = new Uri(addresses[1]).Port;
        Http30Port = new Uri(addresses[2]).Port;

        _app = app;
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
}
