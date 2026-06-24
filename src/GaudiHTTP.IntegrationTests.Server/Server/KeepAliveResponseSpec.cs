using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server;

public sealed class KeepAliveResponseSpec
{
    // Regression for the memory-endurance stall. Http11ServerStateMachine tracked the
    // pipeline-depth limit with a cumulative `_requestsPipelined` counter that was never
    // decremented, so a keep-alive connection silently dropped request number
    // (MaxPipelinedRequests + 1) — no response, no close — and the client timed out.
    [Fact(Timeout = 30000)]
    public async Task Server_should_keep_serving_a_keepalive_connection_past_the_pipeline_depth()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Host.UseTurboHttp(o =>
            o.Listen(IPAddress.Loopback, 0, lo => lo.Protocols = HttpProtocols.Http1));

        await using var app = builder.Build();
        app.MapGet("/data", () => Results.Text("hello-world"));

        await app.StartAsync(TestContext.Current.CancellationToken);
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        using var handler = new SocketsHttpHandler { MaxConnectionsPerServer = 1 };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(address),
            // A stalled response trips this long before the 30 s server/client defaults.
            Timeout = TimeSpan.FromSeconds(3),
        };

        // Far beyond the default MaxPipelinedRequests (16) on a single reused connection.
        for (var i = 0; i < 40; i++)
        {
            using var response = await client.GetAsync("/data", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await app.StopAsync(TestContext.Current.CancellationToken);
    }
}
