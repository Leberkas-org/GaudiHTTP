using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GaudiHTTP.IntegrationTests.Server.Shared;
using GaudiHTTP.Server;
using QuicListenerOptionsServus = Servus.Akka.Transport.QuicListenerOptions;

namespace GaudiHTTP.IntegrationTests.Server.Hosting.Tls;

/// <summary>
/// Real HTTP/3 (QUIC) requests against GaudiServer, driven by a neutral .NET HttpClient.
/// Skipped on platforms without QUIC support.
/// </summary>
[Collection("Infrastructure")]
public sealed class Http3ServerSpec : MultiProtocolTlsServerSpecBase
{
    protected override HttpProtocols ServerProtocols => HttpProtocols.Http3;

    public override async ValueTask InitializeAsync()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Skip("QUIC not supported on this platform");
            return;
        }

        await base.InitializeAsync();
    }

    protected override void ConfigureListener(GaudiServerOptions options, ushort port, X509Certificate2 certificate)
    {
        options.Bind(new QuicListenerOptionsServus
        {
            Host = "127.0.0.1",
            Port = port,
            ServerCertificate = certificate,
            ApplicationProtocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http3 }
        });
    }

    protected override HttpClient CreateHttpClient() => CreateExactVersionTlsClient(HttpVersion.Version30);

    protected override void ConfigureEndpoints(WebApplication app)
    {
        base.ConfigureEndpoints(app);
        app.MapGet("/id/{id:int}", (int id) => Results.Ok(id));
    }

    [Fact(Timeout = 20000)]
    public async Task Http3_should_serve_request_over_h3()
    {
        var response = await Client.GetAsync(Url("/protocol"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version30, response.Version);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal("HTTP/3", System.Text.Json.JsonSerializer.Deserialize<string>(body));
    }

    [Fact(Timeout = 25000)]
    public async Task Http3_should_multiplex_concurrent_requests_on_one_connection()
    {
        var tasks = Enumerable.Range(0, 15)
            .Select(async i =>
            {
                var response = await Client.GetAsync(Url($"/id/{i}"), CancellationToken);
                Assert.Equal(HttpVersion.Version30, response.Version);
                var body = await response.Content.ReadAsStringAsync(CancellationToken);
                return (i, value: System.Text.Json.JsonSerializer.Deserialize<int>(body));
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        foreach (var (i, value) in results)
        {
            Assert.Equal(i, value);
        }
    }
}
