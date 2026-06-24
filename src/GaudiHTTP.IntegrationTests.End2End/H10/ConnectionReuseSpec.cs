using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GaudiHTTP.IntegrationTests.End2End.Shared;

namespace GaudiHTTP.IntegrationTests.End2End.H10;

[Collection("H10")]
public sealed class ConnectionReuseSpec : End2EndSpecBase
{
    protected override Version ProtocolVersion => HttpVersion.Version10;

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/connection-id", (HttpContext ctx) =>
        {
            var remotePort = ctx.Connection.RemotePort;
            return Results.Ok(new { remotePort });
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Http10_should_not_reuse_connections()
    {
        var remotePort1 = await GetRemotePort();
        var remotePort2 = await GetRemotePort();
        var remotePort3 = await GetRemotePort();

        // H1.0 closes the connection after each request, so each request should come from a different ephemeral port
        Assert.NotEqual(remotePort1, remotePort2);
        Assert.NotEqual(remotePort2, remotePort3);
        Assert.NotEqual(remotePort1, remotePort3);
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
