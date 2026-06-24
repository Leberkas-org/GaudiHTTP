using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server;

public sealed class RequestBodySpec
{
    [Fact(Timeout = 15000)]
    public async Task Server_should_accept_request_body_larger_than_legacy_32kb_cap()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Host.UseTurboHttp(o =>
            o.Listen(IPAddress.Loopback, 0, lo => lo.Protocols = HttpProtocols.Http1));

        await using var app = builder.Build();
        app.MapPost("/echo", async ctx =>
        {
            long count = 0;
            var buf = new byte[64 * 1024];
            int read;
            while ((read = await ctx.Request.Body.ReadAsync(buf)) > 0)
            {
                count += read;
            }

            await ctx.Response.WriteAsync(count.ToString());
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        using var client = new HttpClient();
        client.BaseAddress = new Uri(address);
        var payload = new byte[256 * 1024];

        var response =
            await client.PostAsync("/echo", new ByteArrayContent(payload), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((256 * 1024).ToString(),
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        await app.StopAsync(TestContext.Current.CancellationToken);
    }
}