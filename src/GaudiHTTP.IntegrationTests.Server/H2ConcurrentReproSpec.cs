using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GaudiHTTP.IntegrationTests.Server.Shared;
using GaudiHTTP.Server;

namespace GaudiHTTP.IntegrationTests.Server;

/// <summary>
/// Regression test for the H2 concurrent request deadlock (now fixed via per-connection bridge).
/// Verifies that 64+ concurrent H2 GET requests over a single multiplexed connection complete
/// without hanging. Was previously caused by the shared MergeHub/DynamicHub pipeline deadlocking
/// under back-pressure.
/// </summary>
[Collection("ServerStress")]
public sealed class H2ConcurrentReproSpec : MultiProtocolTlsServerSpecBase
{
    protected override HttpProtocols ServerProtocols => HttpProtocols.Http2;

    protected override void ConfigureServer(WebApplicationBuilder builder, ushort port)
    {
        var certificate = CreateSelfSignedCertificate("localhost");
        builder.Host.UseGaudiHttp(options =>
        {
            options.ListenLocalhost(port, listen =>
            {
                listen.UseHttps(certificate);
                listen.Protocols = ServerProtocols;
            });

            // Allow far more concurrent streams than our test concurrency
            // to ensure we don't hit the per-connection stream limit.
            options.Http2.MaxConcurrentStreams = 128;
        });
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/ping/{id:int}", (int id) => Results.Ok(id));
    }

    /// <summary>
    /// Sends 64 concurrent H2 GET requests on one connection and asserts all
    /// respond within a reasonable time. Measures per-request latency to diagnose
    /// pipeline bottlenecks.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task H2_should_process_64_concurrent_requests_on_one_connection()
    {
        const int concurrency = 64;

        using var handler = new SocketsHttpHandler
        {
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
            MaxConnectionsPerServer = 1
        };
        using var client = new HttpClient(handler)
        {
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Timeout = TimeSpan.FromSeconds(8)
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            var response = await client.GetAsync(
                new Uri($"https://127.0.0.1:{Port}/ping/{i}"),
                CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(HttpVersion.Version20, response.Version);
            var body = await response.Content.ReadAsStringAsync(CancellationToken);
            return System.Text.Json.JsonSerializer.Deserialize<int>(body);
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        sw.Stop();

        var sorted = results.Order().ToArray();
        Assert.Equal(Enumerable.Range(0, concurrency).ToArray(), sorted);

        TestContext.Current.SendDiagnosticMessage(
            "H2 {0} concurrent requests completed in {1:N0} ms ({2:N0} req/s)",
            concurrency, sw.ElapsedMilliseconds,
            concurrency * 1000.0 / sw.ElapsedMilliseconds);
    }
}
