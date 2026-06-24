using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GaudiHTTP.Client;
using GaudiHTTP.IntegrationTests.End2End.Shared;

namespace GaudiHTTP.IntegrationTests.End2End.H11;

[Collection("H11")]
public sealed class ConnectionReuseSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version11;

    protected override void ConfigureClientOptions(GaudiClientOptions options)
    {
        // Ensure connection pooling is explicitly enabled with a reasonable idle timeout
        options.PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30);
        options.Http1.MaxConnectionsPerServer = 1;  // Force reuse by allowing only 1 connection
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/connection-id", (HttpContext ctx) =>
        {
            var remotePort = ctx.Connection.RemotePort;
            return Results.Ok(new { remotePort });
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Http11_should_reuse_connections()
    {
        var remotePort1 = await GetRemotePort();
        var remotePort2 = await GetRemotePort();
        var remotePort3 = await GetRemotePort();

        // H1.1 with keep-alive (default) reuses the TCP connection, so all requests should come from the same ephemeral port.
        // All requests through the same IGaudiHttpClient instance should originate from a single, pooled connection.
        Assert.Equal(remotePort1, remotePort2);
        Assert.Equal(remotePort2, remotePort3);
    }

    private async Task<int> GetRemotePort()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/connection-id");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        using var doc = JsonDocument.Parse(body);
        var port = doc.RootElement.GetProperty("remotePort").GetInt32();
        return port;
    }
}
