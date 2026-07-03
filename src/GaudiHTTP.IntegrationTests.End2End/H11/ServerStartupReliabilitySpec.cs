using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Servus.Akka.Transport;
using Servus.Diagnostics;
using GaudiHTTP.Server;

namespace GaudiHTTP.IntegrationTests.End2End.H11;

public sealed class ServerStartupReliabilitySpec
{
    [Fact(Timeout = 30_000)]
    public async Task Server_with_three_listeners_should_respond_to_first_h11_request()
    {
        ThreadPool.SetMinThreads(1024, 1024);
        Servus.Senf.Tracing.Configure(new StderrTraceListener(), TraceLevel.Debug);

        var cert = GenerateSelfSignedCert();
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);

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
            });

            var app = builder.Build();
            app.MapGet("/plaintext", () => Results.Content("Hello, World!", "text/plain"));

            Console.Error.WriteLine("[SPEC] Starting server...");
            await app.StartAsync();

            var addresses = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses
                .ToArray();

            for (var i = 0; i < addresses.Length; i++)
            {
                Console.Error.WriteLine("[SPEC] Listener {0}: {1}", i, addresses[i]);
            }

            var h11Port = new Uri(addresses[0]).Port;
            Console.Error.WriteLine("[SPEC] Sending H1.1 GET /plaintext to port {0}...", h11Port);

            using var handler = new SocketsHttpHandler();
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(5),
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };

            using var response = await client.GetAsync(
                string.Concat("http://127.0.0.1:", h11Port.ToString(), "/plaintext"));

            Console.Error.WriteLine("[SPEC] Response: {0}", response.StatusCode);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal("Hello, World!", body);

            await app.StopAsync();
            await app.DisposeAsync();
        }
        finally
        {
            cert.Dispose();
        }
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

    private sealed class StderrTraceListener : IServusTraceListener
    {
        public bool IsEnabled(TraceLevel level, string category) => true;

        public void Write(in TraceEvent evt)
        {
            Console.Error.WriteLine("[{0}][{1}] {2}#{3:X8}: {4}",
                evt.Level, evt.Category, evt.SourceType, evt.SourceHash, evt.FormatMessage());
        }
    }
}
