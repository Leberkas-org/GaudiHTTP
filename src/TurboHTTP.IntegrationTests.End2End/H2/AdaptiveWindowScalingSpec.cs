using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using TurboHTTP.Client;
using TurboHTTP.IntegrationTests.End2End.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.End2End.H2;

[Collection("H2")]
public sealed class AdaptiveWindowScalingSpec : End2EndSpecBase
{
    private bool _scalingEnabled = true;

    protected override Version ProtocolVersion => HttpVersion.Version20;

    protected override void ConfigureClientOptions(TurboClientOptions options)
    {
        options.Http2.EnableAdaptiveWindowScaling = _scalingEnabled;
        options.Http2.MaxConnectionsPerServer = 1;
    }

    protected override void ConfigureServer(TurboServerOptions options, ushort port, X509Certificate2? cert)
    {
        base.ConfigureServer(options, port, cert);
        options.Limits.MinResponseDataRate = 0;
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/generate", async ctx =>
        {
            var size = int.Parse(ctx.Request.Query["size"]!);
            ctx.Response.ContentType = "application/octet-stream";
            var buffer = new byte[64 * 1024];
            Array.Fill(buffer, (byte)0xCD);
            var remaining = size;
            while (remaining > 0)
            {
                var toWrite = Math.Min(buffer.Length, remaining);
                await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, toWrite), ctx.RequestAborted);
                remaining -= toWrite;
            }
        });
    }

    [Fact(Timeout = 60000)]
    public async Task AdaptiveScaling_should_handle_multiple_concurrent_large_responses()
    {
        const int concurrentRequests = 5;
        const int responseSize = 2 * 1024 * 1024;

        var tasks = new Task<(bool success, int length)>[concurrentRequests];

        for (var i = 0; i < concurrentRequests; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/generate?size={responseSize}");
                var response = await Client.SendAsync(request, TestContext.Current.CancellationToken);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return (false, 0);
                }

                var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

                if (body.Length != responseSize)
                {
                    return (false, body.Length);
                }

                if (!body.All(b => b == 0xCD))
                {
                    return (false, body.Length);
                }

                return (true, body.Length);
            });
        }

        var results = await Task.WhenAll(tasks);

        Assert.True(results.All(r => r.success),
            $"Failures: {string.Join(", ", results.Where(r => !r.success).Select(r => $"len={r.length}"))}");
    }

    [Fact(Timeout = 60000)]
    public async Task AdaptiveScaling_should_transfer_large_body_without_corruption()
    {
        const int responseSize = 4 * 1024 * 1024;

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUri}/generate?size={responseSize}");
        var response = await Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsByteArrayAsync(CancellationToken);

        Assert.Equal(responseSize, body.Length);
        Assert.True(body.All(b => b == 0xCD));
    }
}
