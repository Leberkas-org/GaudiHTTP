using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GaudiHTTP.Client;
using GaudiHTTP.IntegrationTests.End2End.Shared;

namespace GaudiHTTP.IntegrationTests.End2End.H11;

[Collection("H11")]
public sealed class ConnectionPoolingSpec : End2EndSpecBase
{
    private const int MaxConnections = 2;

    protected override Version ProtocolVersion => HttpVersion.Version11;

    protected override void ConfigureClientOptions(GaudiClientOptions options)
    {
        // Cap the pool at MaxConnections and disable pipelining so each in-flight request
        // needs its own connection slot — making the cap observable under concurrency.
        options.Http1.MaxConnectionsPerServer = MaxConnections;
        options.Http1.MaxPipelineDepth = 1;
        options.PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30);
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        // Hold each request briefly so concurrent requests overlap and contend for connections.
        app.MapGet("/slow", async (HttpContext ctx) =>
        {
            await Task.Delay(250);
            return Results.Ok(new { remotePort = ctx.Connection.RemotePort });
        });
    }

    [Fact(Timeout = 20000)]
    public async Task Http11_should_not_exceed_MaxConnectionsPerServer_under_concurrency()
    {
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => GetRemotePort())
            .ToArray();

        var ports = await Task.WhenAll(tasks);
        var distinct = ports.Distinct().Count();

        // The pool must never open more than the configured cap...
        Assert.True(distinct <= MaxConnections, $"Opened {distinct} connections, cap is {MaxConnections}");
        // ...and under this much concurrency it should actually use the full cap (proves real pooling, not a single shared connection).
        Assert.Equal(MaxConnections, distinct);
    }

    private async Task<int> GetRemotePort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/slow");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("remotePort").GetInt32();
    }
}
